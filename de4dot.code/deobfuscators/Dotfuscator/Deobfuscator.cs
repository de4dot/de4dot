/*
    Copyright (C) 2011 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.Dotfuscator {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		const string DEFAULT_REGEX = @"!^[a-z][a-z0-9]{0,2}$&!^A_[0-9]+$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base("df", DEFAULT_REGEX) {
		}

		internal static string ObfuscatorType {
			get { return "Dotfuscator"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.get(),
			});
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = "Dotfuscator";
		Dictionary<MethodReference, StringDecrypterInfo> stringDecrypterMethods = new Dictionary<MethodReference, StringDecrypterInfo>();
		bool foundDotfuscatorAttribute = false;

		class StringDecrypterInfo {
			public MethodDefinition method;
			public int magic;
			public StringDecrypterInfo(MethodDefinition method, int magic) {
				this.method = method;
				this.magic = magic;
			}
		}

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.ObfuscatorType; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override int detect() {
			scanForObfuscator();

			int val = 0;

			if (foundDotfuscatorAttribute)
				val += 100;
			if (stringDecrypterMethods.Count > 0)
				val += 10;

			return val;
		}

		protected override void scanForObfuscatorInternal() {
			findDotfuscatorAttribute();
			findStringDecrypterMethods();
		}

		void findDotfuscatorAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "DotfuscatorAttribute") {
					foundDotfuscatorAttribute = true;
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					initializeVersion(type);
					return;
				}
			}
		}

		void initializeVersion(TypeDefinition attr) {
			var s = DotNetUtils.getCustomArgAsString(getAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+:\d+:\d+:\d+\.\d+\.\d+\.\d+)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = "Dotfuscator " + val.Groups[1].ToString();
		}

		void findStringDecrypterMethods() {
			foreach (var type in module.GetTypes())
				findStringDecrypterMethods(type);
		}

		void findStringDecrypterMethods(TypeDefinition type) {
			foreach (var method in DotNetUtils.findMethods(type.Methods, "System.String", new string[] { "System.String", "System.Int32" })) {
				if (method.Body.HasExceptionHandlers)
					continue;

				var methodCalls = DotNetUtils.getMethodCallCounts(method);
				if (methodCalls.count("System.Char[] System.String::ToCharArray()") != 1)
					continue;
				if (methodCalls.count("System.String System.String::Intern(System.String)") != 1)
					continue;

				DeobfuscatedFile.deobfuscate(method);
				var instructions = method.Body.Instructions;
				for (int i = 0; i <= instructions.Count - 3; i++) {
					var ldci4 = method.Body.Instructions[i];
					if (!DotNetUtils.isLdcI4(ldci4))
						continue;
					if (instructions[i + 1].OpCode.Code != Code.Ldarg_1)
						continue;
					if (instructions[i + 2].OpCode.Code != Code.Add)
						continue;

					var info = new StringDecrypterInfo(method, DotNetUtils.getLdcI4Value(ldci4));
					Log.v("Found string decrypter method: {0}, magic: 0x{1:X8}", info.method, info.magic);
					stringDecrypterMethods[info.method] = info;
					staticStringDecrypter.add(info.method, (method2, args) => decryptString(stringDecrypterMethods[method2], (string)args[0], (int)args[1]));
					break;
				}
			}
		}

		public override void deobfuscateEnd() {
			if (Operations.DecryptStrings != OpDecryptString.None) {
				foreach (var method in stringDecrypterMethods.Keys)
					addMethodToBeRemoved(stringDecrypterMethods[method].method, "String decrypter method");
			}

			base.deobfuscateEnd();
		}

		static string decryptString(StringDecrypterInfo info, string encrypted, int value) {
			char[] chars = encrypted.ToCharArray();
			byte key = (byte)(info.magic + value);
			for (int i = 0; i < chars.Length; i++) {
				char c = chars[i];
				byte b1 = (byte)((byte)c ^ key++);
				byte b2 = (byte)((byte)(c >> 8) ^ key++);
				chars[i] = (char)((b1 << 8) | b2);
			}
			return new string(chars);
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var method in stringDecrypterMethods.Keys)
				list.Add(method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
