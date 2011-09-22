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

namespace de4dot.deobfuscators.Eazfuscator {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public DeobfuscatorInfo()
			: base("ez") {
		}

		internal static string ObfuscatorType {
			get { return "Eazfuscator"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		TypeDefinition decryptStringType;
		MethodDefinition decryptStringMethod;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.ObfuscatorType; }
		}

		public override string Name {
			get { return "Eazfuscator.NET"; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override int detect() {
			scanForObfuscator();
			if (decryptStringMethod != null)
				return 100;
			return 0;
		}

		protected override void scanForObfuscatorInternal() {
			findStringDecrypterMethod();
		}

		void findStringDecrypterMethod() {
			foreach (var type in module.Types) {
				if (!string.IsNullOrEmpty(type.Namespace))
					continue;
				if (DotNetUtils.findFieldType(type, "System.IO.BinaryReader", true) == null)
					continue;
				if (DotNetUtils.findFieldType(type, "System.Collections.Generic.Dictionary`2<System.Int32,System.String>", true) == null)
					continue;

				foreach (var method in type.Methods) {
					if (method.IsStatic && method.HasBody && method.MethodReturnType.ReturnType.FullName == "System.String" &&
						method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Int32") {
						foreach (var instr in method.Body.Instructions) {
							if (instr.OpCode != OpCodes.Callvirt)
								continue;
							var calledMethod = instr.Operand as MethodReference;
							if (calledMethod != null && calledMethod.FullName == "System.IO.Stream System.Reflection.Assembly::GetManifestResourceStream(System.String)") {
								decryptStringType = type;
								decryptStringMethod = method;
								return;
							}
						}
					}
				}
			}
		}

		public override void deobfuscateEnd() {
			if (Operations.DecryptStrings == OpDecryptString.Dynamic) {
				addTypeToBeRemoved(decryptStringType, "String decrypter type");
				findPossibleNamesToRemove(decryptStringMethod);
			}
			addResources("Encrypted strings");

			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (decryptStringMethod != null)
				list.Add(decryptStringMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
