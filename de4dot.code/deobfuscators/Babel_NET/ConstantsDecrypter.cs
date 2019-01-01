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
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ConstantsDecrypter {
		ModuleDefMD module;
		ResourceDecrypter resourceDecrypter;
		InitializedDataCreator initializedDataCreator;
		TypeDef decrypterType;
		MethodDef int32Decrypter;
		MethodDef int64Decrypter;
		MethodDef singleDecrypter;
		MethodDef doubleDecrypter;
		MethodDef arrayDecrypter;
		EmbeddedResource encryptedResource;
		int[] decryptedInts;
		long[] decryptedLongs;
		float[] decryptedFloats;
		double[] decryptedDoubles;

		public bool Detected => decrypterType != null;
		public bool CanDecrypt => encryptedResource != null;
		public Resource Resource => encryptedResource;
		public TypeDef Type => decrypterType;
		public MethodDef Int32Decrypter => int32Decrypter;
		public MethodDef Int64Decrypter => int64Decrypter;
		public MethodDef SingleDecrypter => singleDecrypter;
		public MethodDef DoubleDecrypter => doubleDecrypter;
		public MethodDef ArrayDecrypter => arrayDecrypter;

		public ConstantsDecrypter(ModuleDefMD module, ResourceDecrypter resourceDecrypter, InitializedDataCreator initializedDataCreator) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
			this.initializedDataCreator = initializedDataCreator;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (!IsConstantDecrypter(type))
					continue;

				int32Decrypter = DotNetUtils.GetMethod(type, "System.Int32", "(System.Int32)");
				int64Decrypter = DotNetUtils.GetMethod(type, "System.Int64", "(System.Int32)");
				singleDecrypter = DotNetUtils.GetMethod(type, "System.Single", "(System.Int32)");
				doubleDecrypter = DotNetUtils.GetMethod(type, "System.Double", "(System.Int32)");
				arrayDecrypter = DotNetUtils.GetMethod(type, "System.Array", "(System.Byte[])");
				decrypterType = type;
				return;
			}
		}

		bool IsConstantDecrypter(TypeDef type) {
			if (type.HasEvents)
				return false;
			if (type.NestedTypes.Count != 1)
				return false;

			var nested = type.NestedTypes[0];
			if (!CheckNestedFields(nested))
				return false;

			resourceDecrypter.DecryptMethod = ResourceDecrypter.FindDecrypterMethod(nested.FindMethod(".ctor"));

			if (DotNetUtils.GetMethod(type, "System.Int32", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Int64", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Single", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Double", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Array", "(System.Byte[])") == null)
				return false;

			return true;
		}

		static string[] requiredTypes = new string[] {
			"System.Int32[]",
			"System.Int64[]",
			"System.Single[]",
			"System.Double[]",
		};
		bool CheckNestedFields(TypeDef nested) {
			if (!new FieldTypes(nested).All(requiredTypes))
				return false;
			foreach (var field in nested.Fields) {
				if (new SigComparer().Equals(nested, field.FieldSig.GetFieldType()))
					return true;
			}
			return false;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (decrypterType == null)
				return;

			encryptedResource = BabelUtils.FindEmbeddedResource(module, decrypterType, simpleDeobfuscator, deob);
			if (encryptedResource == null) {
				Logger.w("Could not find encrypted constants resource");
				return;
			}

			var decrypted = resourceDecrypter.Decrypt(encryptedResource.CreateReader().ToArray());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int count;

			count = reader.ReadInt32();
			decryptedInts = new int[count];
			while (count-- > 0)
				decryptedInts[count] = reader.ReadInt32();

			count = reader.ReadInt32();
			decryptedLongs = new long[count];
			while (count-- > 0)
				decryptedLongs[count] = reader.ReadInt64();

			count = reader.ReadInt32();
			decryptedFloats = new float[count];
			while (count-- > 0)
				decryptedFloats[count] = reader.ReadSingle();

			count = reader.ReadInt32();
			decryptedDoubles = new double[count];
			while (count-- > 0)
				decryptedDoubles[count] = reader.ReadDouble();
		}

		public int DecryptInt32(int index) => decryptedInts[index];
		public long DecryptInt64(int index) => decryptedLongs[index];
		public float DecryptSingle(int index) => decryptedFloats[index];
		public double DecryptDouble(int index) => decryptedDoubles[index];

		struct ArrayInfo {
			public FieldDef encryptedField;
			public SZArraySig arrayType;
			public int start, len;

			public ArrayInfo(int start, int len, FieldDef encryptedField, SZArraySig arrayType) {
				this.start = start;
				this.len = len;
				this.encryptedField = encryptedField;
				this.arrayType = arrayType;
			}
		}

		public void Deobfuscate(Blocks blocks) {
			if (arrayDecrypter == null)
				return;

			var infos = new List<ArrayInfo>();
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				infos.Clear();
				for (int i = 0; i < instrs.Count - 6; i++) {
					int index = i;

					var ldci4 = instrs[index++];
					if (!ldci4.IsLdcI4())
						continue;

					var newarr = instrs[index++];
					if (newarr.OpCode.Code != Code.Newarr)
						continue;
					if (newarr.Operand == null || newarr.Operand.ToString() != "System.Byte")
						continue;

					if (instrs[index++].OpCode.Code != Code.Dup)
						continue;

					var ldtoken = instrs[index++];
					if (ldtoken.OpCode.Code != Code.Ldtoken)
						continue;
					var field = ldtoken.Operand as FieldDef;
					if (field == null)
						continue;

					var call1 = instrs[index++];
					if (call1.OpCode.Code != Code.Call && call1.OpCode.Code != Code.Callvirt)
						continue;
					if (!DotNetUtils.IsMethod(call1.Operand as IMethod, "System.Void", "(System.Array,System.RuntimeFieldHandle)"))
						continue;

					var call2 = instrs[index++];
					if (call2.OpCode.Code != Code.Call && call2.OpCode.Code != Code.Callvirt)
						continue;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(call2.Operand as IMethod, arrayDecrypter))
						continue;

					var castclass = instrs[index++];
					if (castclass.OpCode.Code != Code.Castclass)
						continue;
					var arrayType = (castclass.Operand as ITypeDefOrRef).TryGetSZArraySig();
					if (arrayType == null)
						continue;
					if (arrayType.Next.ElementType.GetPrimitiveSize() == -1) {
						Logger.w("Can't decrypt non-primitive type array in method {0:X8}", blocks.Method.MDToken.ToInt32());
						continue;
					}

					infos.Add(new ArrayInfo(i, index - i, field, arrayType));
				}

				infos.Reverse();
				foreach (var info in infos) {
					var elemSize = info.arrayType.Next.ElementType.GetPrimitiveSize();
					var decrypted = DecryptArray(info.encryptedField.InitialValue, elemSize);

					initializedDataCreator.AddInitializeArrayCode(block, info.start, info.len, info.arrayType.Next.ToTypeDefOrRef(), decrypted);
					Logger.v("Decrypted {0} array: {1} elements", info.arrayType.Next.ToString(), decrypted.Length / elemSize);
				}
			}
		}

		byte[] DecryptArray(byte[] encryptedData, int elemSize) {
			var decrypted = resourceDecrypter.Decrypt(encryptedData);
			var ary = (Array)new BinaryFormatter().Deserialize(new MemoryStream(decrypted));
			if (ary is byte[])
				return (byte[])ary;
			var newAry = new byte[ary.Length * elemSize];
			Buffer.BlockCopy(ary, 0, newAry, 0, newAry.Length);
			return newAry;
		}
	}
}
