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
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class StringsEncoderInfo {
		// SmartAssembly.HouseOfCards.Strings, the class that creates the string decrypter
		// delegates
		public TypeDefinition StringsType { get; set; }
		public TypeDefinition GetStringDelegate { get; set; }
		public MethodDefinition CreateStringDelegateMethod { get; set; }

		// The class that decodes the strings. Called by the strings delegate or normal code.
		public TypeDefinition StringDecrypterClass { get; set; }
	}

	class StringEncoderClassFinder {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IList<StringsEncoderInfo> stringsEncoderInfos = new List<StringsEncoderInfo>();

		public IList<StringsEncoderInfo> StringsEncoderInfos {
			get { return stringsEncoderInfos; }
		}

		public StringEncoderClassFinder(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		TypeDefinition getType(TypeReference typeReference) {
			return DotNetUtils.getType(module, typeReference);
		}

		public void find() {
			findHouseOfCardsStrings_v2();
			if (stringsEncoderInfos.Count == 0)
				findHouseOfCardsStrings_v1();

			findStringDecrypterClasses();
		}

		// Finds SmartAssembly.HouseOfCards.Strings. It's the class that creates the string
		// decrypter delegates.
		void findHouseOfCardsStrings_v2() {
			foreach (var type in module.Types) {
				if (type.Methods.Count != 1)
					continue;
				foreach (var method in DotNetUtils.findMethods(type.Methods, "System.Void", new string[] { "System.Type" })) {
					if (checkDelegateCreatorMethod(type, method))
						break;
				}
			}
		}

		void findHouseOfCardsStrings_v1() {
			foreach (var type in module.Types) {
				if (type.Methods.Count != 1)
					continue;
				foreach (var method in DotNetUtils.findMethods(type.Methods, "System.Void", new string[] { })) {
					if (checkDelegateCreatorMethod(type, method))
						break;
				}
			}
		}

		bool checkDelegateCreatorMethod(TypeDefinition type, MethodDefinition method) {
			simpleDeobfuscator.deobfuscate(method);

			var getStringDelegate = findGetStringDelegate(method);
			if (getStringDelegate == null)
				return false;

			var stringDecrypterClass = findStringDecrypterClass(method);
			if (stringDecrypterClass == null)
				return false;

			stringsEncoderInfos.Add(new StringsEncoderInfo {
				StringsType = type,
				GetStringDelegate = getStringDelegate,
				StringDecrypterClass = stringDecrypterClass,
				CreateStringDelegateMethod = method,
			});

			return true;
		}

		// Finds the SmartAssembly.Delegates.GetString delegate
		TypeDefinition findGetStringDelegate(MethodDefinition stringsCreateDelegateMethod) {
			if (!stringsCreateDelegateMethod.HasBody)
				return null;

			foreach (var ldtoken in stringsCreateDelegateMethod.Body.Instructions) {
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var typeToken = ldtoken.Operand as TypeReference;
				if (typeToken == null)
					continue;
				var delegateType = getType(typeToken);
				if (!DotNetUtils.derivesFromDelegate(delegateType))
					continue;
				var invoke = DotNetUtils.getMethod(delegateType, "Invoke");
				if (invoke == null || !DotNetUtils.isMethod(invoke, "System.String", "(System.Int32)"))
					continue;

				return delegateType;
			}

			return null;
		}

		// Finds the SmartAssembly.StringsEncoding.Strings class. This class decrypts the
		// strings in the resources. It gets called by the SmartAssembly.Delegates.GetString
		// delegate instances which were created by SmartAssembly.HouseOfCards.Strings.
		TypeDefinition findStringDecrypterClass(MethodDefinition stringsCreateDelegateMethod) {
			if (!stringsCreateDelegateMethod.HasBody)
				return null;

			foreach (var ldtoken in stringsCreateDelegateMethod.Body.Instructions) {
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var typeToken = ldtoken.Operand as TypeReference;
				if (typeToken == null)
					continue;
				var type = getType(typeToken);
				if (type == null || DotNetUtils.derivesFromDelegate(type))
					continue;
				if (!couldBeStringDecrypterClass(type))
					continue;

				return type;
			}

			return null;
		}

		void findStringDecrypterClasses() {
			var foundClasses = new Dictionary<TypeDefinition, bool>();
			foreach (var info in stringsEncoderInfos)
				foundClasses[info.StringDecrypterClass] = true;

			foreach (var type in module.Types) {
				if (!foundClasses.ContainsKey(type) && couldBeStringDecrypterClass(type)) {
					stringsEncoderInfos.Add(new StringsEncoderInfo {
						StringsType = null,
						GetStringDelegate = null,
						StringDecrypterClass = type,
					});
				}
			}
		}

		static string[] fields1x = new string[] {
			"System.IO.Stream",
		};
		static string[] fields2x = new string[] {
			"System.IO.Stream",
			"System.Int32",
		};
		static string[] fields3x = new string[] {
			"System.Byte[]",
			"System.Int32",
		};
		bool couldBeStringDecrypterClass(TypeDefinition type) {
			var fields = new FieldTypes(type);
			if (fields.exists("System.Collections.Hashtable") ||
				fields.exists("System.Collections.Generic.Dictionary`2<System.Int32,System.String>") ||
				fields.exactly(fields3x)) {
				if (DotNetUtils.getMethod(type, ".cctor") == null)
					return false;
			}
			else if (fields.exactly(fields1x) || fields.exactly(fields2x)) {
			}
			else
				return false;

			var methods = new List<MethodDefinition>(DotNetUtils.getNormalMethods(type));
			if (methods.Count != 1)
				return false;
			var method = methods[0];
			if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
				return false;
			if (!method.IsStatic || !method.HasBody)
				return false;

			return true;
		}
	}
}
