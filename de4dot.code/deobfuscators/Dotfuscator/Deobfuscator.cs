/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Dotfuscator {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Dotfuscator";
		public const string THE_TYPE = "df";
		const string DEFAULT_REGEX = @"!^[a-z][a-z0-9]{0,2}$&!^A_[0-9]+$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
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

		StringDecrypter stringDecrypter;
		bool foundDotfuscatorAttribute = false;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			if (stringDecrypter.Detected)
				val += 100;
			if (foundDotfuscatorAttribute)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			findDotfuscatorAttribute();
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

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+(?::\d+)*\.\d+(?:\.\d+)*)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = "Dotfuscator " + val.Groups[1].ToString();
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
			foreach (var info in stringDecrypter.StringDecrypterInfos)
				staticStringInliner.add(info.method, (method, gim, args) => stringDecrypter.decrypt(method, (string)args[0], (int)args[1]));
			DeobfuscatedFile.stringDecryptersAdded();
		}

		public override void deobfuscateEnd() {
			if (CanRemoveStringDecrypterType)
				addMethodsToBeRemoved(stringDecrypter.StringDecrypters, "String decrypter method");

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in stringDecrypter.StringDecrypters)
				list.Add(method.MetadataToken.ToInt32());
			return list;
		}
	}
}
