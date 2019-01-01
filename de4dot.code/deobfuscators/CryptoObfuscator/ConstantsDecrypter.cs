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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ConstantsDecrypter {
		ModuleDefMD module;
		TypeDef decrypterType;
		MethodDef methodI4;
		MethodDef methodI8;
		MethodDef methodR4;
		MethodDef methodR8;
		MethodDef methodArray;
		InitializedDataCreator initializedDataCreator;
		EmbeddedResource encryptedResource;
		byte[] constantsData;

		public TypeDef Type => decrypterType;
		public EmbeddedResource Resource => encryptedResource;
		public MethodDef Int32Decrypter => methodI4;
		public MethodDef Int64Decrypter => methodI8;
		public MethodDef SingleDecrypter => methodR4;
		public MethodDef DoubleDecrypter => methodR8;
		public bool Detected => decrypterType != null;

		public ConstantsDecrypter(ModuleDefMD module, InitializedDataCreator initializedDataCreator) {
			this.module = module;
			this.initializedDataCreator = initializedDataCreator;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (!CheckType(type))
					continue;

				decrypterType = type;
				return;
			}
		}

		static readonly string[] requiredTypes = new string[] {
			"System.Byte[]",
		};
		bool CheckType(TypeDef type) {
			if (type.Methods.Count != 7)
				return false;
			if (type.Fields.Count < 1 || type.Fields.Count > 2)
				return false;
			if (!new FieldTypes(type).All(requiredTypes))
				return false;
			if (!CheckMethods(type))
				return false;

			return true;
		}

		bool CheckMethods(TypeDef type) {
			methodI4 = DotNetUtils.GetMethod(type, "System.Int32", "(System.Int32)");
			methodI8 = DotNetUtils.GetMethod(type, "System.Int64", "(System.Int32)");
			methodR4 = DotNetUtils.GetMethod(type, "System.Single", "(System.Int32)");
			methodR8 = DotNetUtils.GetMethod(type, "System.Double", "(System.Int32)");
			methodArray = DotNetUtils.GetMethod(type, "System.Void", "(System.Array,System.Int32)");

			return methodI4 != null && methodI8 != null &&
				methodR4 != null && methodR8 != null &&
				methodArray != null;
		}

		public void Initialize(ResourceDecrypter resourceDecrypter) {
			if (decrypterType == null)
				return;

			var cctor = decrypterType.FindStaticConstructor();
			encryptedResource = CoUtils.GetResource(module, DotNetUtils.GetCodeStrings(cctor));

			//if the return value is null, it is possible that resource name is encrypted
			if (encryptedResource == null) {
				var Resources = new string[] { CoUtils.DecryptResourceName(module, cctor) };
				encryptedResource = CoUtils.GetResource(module, Resources);
			}

			constantsData = resourceDecrypter.Decrypt(encryptedResource.CreateReader().AsStream());
		}

		public int DecryptInt32(int index) => BitConverter.ToInt32(constantsData, index);
		public long DecryptInt64(int index) => BitConverter.ToInt64(constantsData, index);
		public float DecryptSingle(int index) => BitConverter.ToSingle(constantsData, index);
		public double DecryptDouble(int index) => BitConverter.ToDouble(constantsData, index);

		struct ArrayInfo {
			public CorLibTypeSig arrayType;
			public int start, len;
			public int arySize, index;

			public ArrayInfo(int start, int len, CorLibTypeSig arrayType, int arySize, int index) {
				this.start = start;
				this.len = len;
				this.arrayType = arrayType;
				this.arySize = arySize;
				this.index = index;
			}
		}

		public void Deobfuscate(Blocks blocks) {
			var infos = new List<ArrayInfo>();
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				infos.Clear();

				for (int i = 0; i < instrs.Count - 5; i++) {
					int index = i;

					var ldci4_arySize = instrs[index++];
					if (!ldci4_arySize.IsLdcI4())
						continue;

					var newarr = instrs[index++];
					if (newarr.OpCode.Code != Code.Newarr)
						continue;
					var arrayType = module.CorLibTypes.GetCorLibTypeSig(newarr.Operand as ITypeDefOrRef);
					if (arrayType == null)
						continue;

					if (instrs[index++].OpCode.Code != Code.Dup)
						continue;

					var ldci4_index = instrs[index++];
					if (!ldci4_index.IsLdcI4())
						continue;

					var call = instrs[index++];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(call.Operand as IMethod, methodArray))
						continue;

					if (arrayType.ElementType.GetPrimitiveSize() == -1) {
						Logger.w("Can't decrypt non-primitive type array in method {0:X8}", blocks.Method.MDToken.ToInt32());
						continue;
					}

					infos.Add(new ArrayInfo(i, index - i, arrayType, ldci4_arySize.GetLdcI4Value(),
								ldci4_index.GetLdcI4Value()));
				}

				infos.Reverse();
				foreach (var info in infos) {
					var elemSize = info.arrayType.ElementType.GetPrimitiveSize();
					var decrypted = DecryptArray(info);
					initializedDataCreator.AddInitializeArrayCode(block, info.start, info.len, info.arrayType.ToTypeDefOrRef(), decrypted);
					Logger.v("Decrypted {0} array: {1} elements", info.arrayType.ToString(), decrypted.Length / elemSize);
				}
			}
		}

		byte[] DecryptArray(ArrayInfo aryInfo) {
			var ary = new byte[aryInfo.arySize * aryInfo.arrayType.ElementType.GetPrimitiveSize()];
			int dataIndex = aryInfo.index;
			int len = DeobUtils.ReadVariableLengthInt32(constantsData, ref dataIndex);
			Buffer.BlockCopy(constantsData, dataIndex, ary, 0, len);
			return ary;
		}
	}
}
