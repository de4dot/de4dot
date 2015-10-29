/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class StringsEncoderInfo {
		// SmartAssembly.HouseOfCards.Strings, the class that creates the string decrypter
		// delegates
		public TypeDef StringsType { get; set; }
		public TypeDef GetStringDelegate { get; set; }
		public MethodDef CreateStringDelegateMethod { get; set; }

		// The class that decodes the strings. Called by the strings delegate or normal code.
		public TypeDef StringDecrypterClass { get; set; }
	}

	class StringEncoderClassFinder {
		ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IList<StringsEncoderInfo> stringsEncoderInfos = new List<StringsEncoderInfo>();

		public IList<StringsEncoderInfo> StringsEncoderInfos {
			get { return stringsEncoderInfos; }
		}

		public StringEncoderClassFinder(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		TypeDef GetType(ITypeDefOrRef typeRef) {
			return DotNetUtils.GetType(module, typeRef);
		}

		public void Find() {
			FindHouseOfCardsStrings_v2();
			if (stringsEncoderInfos.Count == 0)
				FindHouseOfCardsStrings_v1();

			FindStringDecrypterClasses();
		}

		// Finds SmartAssembly.HouseOfCards.Strings. It's the class that creates the string
		// decrypter delegates.
		void FindHouseOfCardsStrings_v2() {
			foreach (var type in module.Types) {
				if (type.Methods.Count != 1)
					continue;
				foreach (var method in DotNetUtils.FindMethods(type.Methods, "System.Void", new string[] { "System.Type" })) {
					if (CheckDelegateCreatorMethod(type, method))
						break;
				}
			}
		}

		void FindHouseOfCardsStrings_v1() {
			foreach (var type in module.Types) {
				if (type.Methods.Count != 1)
					continue;
				foreach (var method in DotNetUtils.FindMethods(type.Methods, "System.Void", new string[] { })) {
					if (CheckDelegateCreatorMethod(type, method))
						break;
				}
			}
		}

		bool CheckDelegateCreatorMethod(TypeDef type, MethodDef method) {
			simpleDeobfuscator.Deobfuscate(method);

			var getStringDelegate = FindGetStringDelegate(method);
			if (getStringDelegate == null)
				return false;

			var stringDecrypterClass = FindStringDecrypterClass(method);
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
		TypeDef FindGetStringDelegate(MethodDef stringsCreateDelegateMethod) {
			if (!stringsCreateDelegateMethod.HasBody)
				return null;

			foreach (var ldtoken in stringsCreateDelegateMethod.Body.Instructions) {
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var typeToken = ldtoken.Operand as ITypeDefOrRef;
				if (typeToken == null)
					continue;
				var delegateType = GetType(typeToken);
				if (!DotNetUtils.DerivesFromDelegate(delegateType))
					continue;
				var invoke = delegateType.FindMethod("Invoke");
				if (invoke == null || !DotNetUtils.IsMethod(invoke, "System.String", "(System.Int32)"))
					continue;

				return delegateType;
			}

			return null;
		}

		// Finds the SmartAssembly.StringsEncoding.Strings class. This class decrypts the
		// strings in the resources. It gets called by the SmartAssembly.Delegates.GetString
		// delegate instances which were created by SmartAssembly.HouseOfCards.Strings.
		TypeDef FindStringDecrypterClass(MethodDef stringsCreateDelegateMethod) {
			if (!stringsCreateDelegateMethod.HasBody)
				return null;

			foreach (var ldtoken in stringsCreateDelegateMethod.Body.Instructions) {
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var typeToken = ldtoken.Operand as ITypeDefOrRef;
				if (typeToken == null)
					continue;
				var type = GetType(typeToken);
				if (type == null || DotNetUtils.DerivesFromDelegate(type))
					continue;
				if (!CouldBeStringDecrypterClass(type))
					continue;

				return type;
			}

			return null;
		}

		void FindStringDecrypterClasses() {
			var foundClasses = new Dictionary<TypeDef, bool>();
			foreach (var info in stringsEncoderInfos)
				foundClasses[info.StringDecrypterClass] = true;

			foreach (var type in module.Types) {
				if (!foundClasses.ContainsKey(type) && CouldBeStringDecrypterClass(type)) {
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
		bool CouldBeStringDecrypterClass(TypeDef type) {
			var fields = new FieldTypes(type);
			if (fields.Exists("System.Collections.Hashtable") ||
				fields.Exists("System.Collections.Generic.Dictionary`2<System.Int32,System.String>") ||
				fields.Exactly(fields3x)) {
				if (type.FindStaticConstructor() == null)
					return false;
			}
			else if (fields.Exactly(fields1x) || fields.Exactly(fields2x)) {
			}
			else
				return false;

			var methods = new List<MethodDef>(DotNetUtils.GetNormalMethods(type));
			if (methods.Count != 1)
				return false;
			var method = methods[0];
			if (!DotNetUtils.IsMethod(method, "System.String", "(System.Int32)"))
				return false;
			if (!method.IsStatic || !method.HasBody)
				return false;

			return true;
		}
	}
}
