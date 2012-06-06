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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class StringDecrypter {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		TypeDefinition decrypterType;
		EmbeddedResource encryptedResource;
		IDecrypterInfo decrypterInfo;

		interface IDecrypterInfo {
			MethodDefinition Decrypter { get; }
			bool NeedsResource { get; }
			void initialize(ModuleDefinition module, EmbeddedResource resource);
			string decrypt(object[] args);
		}

		// Babel .NET 2.x
		class DecrypterInfoV1 : IDecrypterInfo {
			public MethodDefinition Decrypter { get; set; }
			public bool NeedsResource {
				get { return false; }
			}

			public void initialize(ModuleDefinition module, EmbeddedResource resource) {
			}

			public string decrypt(object[] args) {
				return decrypt((string)args[0], (int)args[1]);
			}

			string decrypt(string s, int k) {
				var sb = new StringBuilder(s.Length);
				foreach (var c in s)
					sb.Append((char)(c ^ k));
				return sb.ToString();
			}
		}

		class DecrypterInfoV2 : IDecrypterInfo {
			byte[] key;

			public MethodDefinition Decrypter { get; set; }
			public bool NeedsResource {
				get { return true; }
			}

			public void initialize(ModuleDefinition module, EmbeddedResource resource) {
				key = resource.GetResourceData();
				if (key.Length != 0x100)
					throw new ApplicationException(string.Format("Unknown key length: {0}", key.Length));
			}

			public string decrypt(object[] args) {
				return decrypt((string)args[0], (int)args[1]);
			}

			string decrypt(string s, int k) {
				var sb = new StringBuilder(s.Length);
				byte b = key[(byte)k];
				foreach (var c in s)
					sb.Append((char)(c ^ (b | k)));
				return sb.ToString();
			}
		}

		class DecrypterInfoV3 : IDecrypterInfo {
			Dictionary<int, string> offsetToString = new Dictionary<int, string>();

			public int OffsetMagic { get; set; }
			public MethodDefinition Decrypter { get; set; }
			public bool NeedsResource {
				get { return true; }
			}

			public void initialize(ModuleDefinition module, EmbeddedResource resource) {
				var decrypted = new ResourceDecrypter(module).decrypt(resource.GetResourceData());
				var reader = new BinaryReader(new MemoryStream(decrypted));
				while (reader.BaseStream.Position < reader.BaseStream.Length)
					offsetToString[(int)reader.BaseStream.Position] = reader.ReadString();
			}

			public string decrypt(object[] args) {
				return decrypt((int)args[0]);
			}

			string decrypt(int offset) {
				return offsetToString[offset ^ OffsetMagic];
			}
		}

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public MethodDefinition DecryptMethod {
			get { return decrypterInfo == null ? null : decrypterInfo.Decrypter; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			this.simpleDeobfuscator = simpleDeobfuscator;
			foreach (var type in module.Types) {
				var info = checkDecrypterType(type);
				if (info == null)
					continue;

				decrypterType = type;
				decrypterInfo = info;
				return;
			}
		}

		IDecrypterInfo checkDecrypterType(TypeDefinition type) {
			if (type.HasEvents)
				return null;
			if (type.NestedTypes.Count > 2)
				return null;
			if (type.Fields.Count > 1)
				return null;

			foreach (var nested in type.NestedTypes) {
				var info = checkNested(type, nested);
				if (info != null)
					return info;
			}

			return checkDecrypterTypeBabel2x(type);
		}

		IDecrypterInfo checkDecrypterTypeBabel2x(TypeDefinition type) {
			if (type.HasEvents || type.HasProperties || type.HasNestedTypes)
				return null;
			if (type.HasFields || type.Methods.Count != 1)
				return null;
			var decrypter = type.Methods[0];
			if (!checkDecryptMethodBabel2x(decrypter))
				return null;

			return new DecrypterInfoV1 { Decrypter = decrypter };
		}

		bool checkDecryptMethodBabel2x(MethodDefinition method) {
			if (!method.IsStatic || !method.IsPublic)
				return false;
			if (method.Body == null)
				return false;
			if (method.Name == ".cctor")
				return false;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.String,System.Int32)"))
				return false;

			int stringLength = 0, stringToCharArray = 0, stringCtor = 0;
			foreach (var instr in method.Body.Instructions) {
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod == null)
					continue;

				switch (instr.OpCode.Code) {
				case Code.Call:
				case Code.Callvirt:
					if (calledMethod.FullName == "System.Int32 System.String::get_Length()")
						stringLength++;
					else if (calledMethod.FullName == "System.Char[] System.String::ToCharArray()")
						stringToCharArray++;
					else
						return false;
					break;

				case Code.Newobj:
					if (calledMethod.FullName == "System.Void System.String::.ctor(System.Char[])")
						stringCtor++;
					else
						return false;
					break;

				default:
					continue;
				}
			}

			return stringLength == 1 && stringToCharArray == 1 && stringCtor == 1;
		}

		IDecrypterInfo checkNested(TypeDefinition type, TypeDefinition nested) {
			if (nested.HasProperties || nested.HasEvents)
				return null;

			if (DotNetUtils.getMethod(nested, ".ctor") == null)
				return null;

			if (nested.Fields.Count == 1) {
				// 4.0+

				if (!MemberReferenceHelper.compareTypes(nested.Fields[0].FieldType, nested))
					return null;

				var decrypterBuilderMethod = DotNetUtils.getMethod(nested, "System.Reflection.Emit.MethodBuilder", "(System.Reflection.Emit.TypeBuilder)");
				if (decrypterBuilderMethod == null)
					return null;

				var nestedDecrypter = DotNetUtils.getMethod(nested, "System.String", "(System.Int32)");
				if (nestedDecrypter == null || nestedDecrypter.IsStatic)
					return null;
				var decrypter = DotNetUtils.getMethod(type, "System.String", "(System.Int32)");
				if (decrypter == null || !decrypter.IsStatic)
					return null;

				simpleDeobfuscator.deobfuscate(decrypterBuilderMethod);
				return new DecrypterInfoV3 {
					Decrypter = decrypter,
					OffsetMagic = getOffsetMagic(decrypterBuilderMethod),
				};
			}
			else if (nested.Fields.Count == 2) {
				// 3.0 - 3.5

				if (checkFields(nested, "System.Collections.Hashtable", nested)) {
					// 3.5
					var nestedDecrypter = DotNetUtils.getMethod(nested, "System.String", "(System.Int32)");
					if (nestedDecrypter == null || nestedDecrypter.IsStatic)
						return null;
					var decrypter = DotNetUtils.getMethod(type, "System.String", "(System.Int32)");
					if (decrypter == null || !decrypter.IsStatic)
						return null;

					return new DecrypterInfoV3 { Decrypter = decrypter };
				}
				else if (checkFields(nested, "System.Byte[]", nested)) {
					// 3.0
					var nestedDecrypter = DotNetUtils.getMethod(nested, "System.String", "(System.String,System.Int32)");
					if (nestedDecrypter == null || nestedDecrypter.IsStatic)
						return null;
					var decrypter = DotNetUtils.getMethod(type, "System.String", "(System.String,System.Int32)");
					if (decrypter == null || !decrypter.IsStatic)
						return null;

					return new DecrypterInfoV2 { Decrypter = decrypter };
				}
				else
					return null;
			}

			return null;
		}

		static int getOffsetMagic(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;

				var ldsfld1 = instrs[index++];
				if (ldsfld1.OpCode.Code != Code.Ldsfld)
					continue;

				var ldci4 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var callvirt = instrs[index++];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Void System.Reflection.Emit.ILGenerator::Emit(System.Reflection.Emit.OpCode,System.Int32)")
					continue;

				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;

				var ldsfld2 = instrs[index++];
				if (ldsfld2.OpCode.Code != Code.Ldsfld)
					continue;
				var field = ldsfld2.Operand as FieldReference;
				if (field == null)
					continue;
				if (field.FullName != "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Xor")
					continue;

				// Here if Babel.NET 5.5
				return DotNetUtils.getLdcI4Value(ldci4);
			}

			// Here if Babel.NET <= 5.0
			return 0;
		}

		bool checkFields(TypeDefinition type, string fieldType1, TypeDefinition fieldType2) {
			if (type.Fields.Count != 2)
				return false;
			if (type.Fields[0].FieldType.FullName != fieldType1 &&
				type.Fields[1].FieldType.FullName != fieldType1)
				return false;
			if (!MemberReferenceHelper.compareTypes(type.Fields[0].FieldType, fieldType2) &&
				!MemberReferenceHelper.compareTypes(type.Fields[1].FieldType, fieldType2))
				return false;
			return true;
		}

		public void initialize() {
			if (decrypterType == null)
				return;
			if (encryptedResource != null)
				return;

			if (decrypterInfo.NeedsResource) {
				encryptedResource = BabelUtils.findEmbeddedResource(module, decrypterType);
				if (encryptedResource == null)
					return;
			}

			decrypterInfo.initialize(module, encryptedResource);
		}

		public string decrypt(object[] args) {
			return decrypterInfo.decrypt(args);
		}
	}
}
