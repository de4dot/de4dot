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
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	public class InitializedDataCreator {
		ModuleDef module;
		Dictionary<long, TypeDef> sizeToArrayType = new Dictionary<long, TypeDef>();
		TypeDef ourType;
		TypeDefOrRefSig valueType;
		int unique = 0;
		MemberRef initializeArrayMethod;

		public MemberRef InitializeArrayMethod {
			get { return CreateInitializeArrayMethod(); }
		}

		public InitializedDataCreator(ModuleDef module) {
			this.module = module;
		}

		MemberRef CreateInitializeArrayMethod() {
			if (initializeArrayMethod == null) {
				var runtimeHelpersType = DotNetUtils.FindOrCreateTypeRef(module, module.CorLibTypes.AssemblyRef, "System.Runtime.CompilerServices", "RuntimeHelpers", false);
				var systemArrayType = DotNetUtils.FindOrCreateTypeRef(module, module.CorLibTypes.AssemblyRef, "System", "Array", false);
				var runtimeFieldHandleType = DotNetUtils.FindOrCreateTypeRef(module, module.CorLibTypes.AssemblyRef, "System", "RuntimeFieldHandle", true);
				var methodSig = MethodSig.CreateStatic(module.CorLibTypes.Void, systemArrayType, runtimeFieldHandleType);
				initializeArrayMethod = module.UpdateRowId(new MemberRefUser(module, "InitializeArray", methodSig, runtimeHelpersType.TypeDefOrRef));
			}
			return initializeArrayMethod;
		}

		public void AddInitializeArrayCode(Block block, int start, int numToRemove, ITypeDefOrRef elementType, byte[] data) {
			int index = start;
			block.Replace(index++, numToRemove, Instruction.CreateLdcI4(data.Length / elementType.ToTypeSig().ElementType.GetPrimitiveSize()));
			block.Insert(index++, OpCodes.Newarr.ToInstruction(elementType));
			block.Insert(index++, OpCodes.Dup.ToInstruction());
			block.Insert(index++, OpCodes.Ldtoken.ToInstruction((IField)Create(data)));
			block.Insert(index++, OpCodes.Call.ToInstruction((IMethod)InitializeArrayMethod));
		}

		void CreateOurType() {
			if (ourType != null)
				return;

			ourType = new TypeDefUser("", string.Format("<PrivateImplementationDetails>{0}", GetModuleId()), module.CorLibTypes.Object.TypeDefOrRef);
			ourType.Attributes = TypeAttributes.NotPublic | TypeAttributes.AutoLayout |
							TypeAttributes.Class | TypeAttributes.AnsiClass;
			module.UpdateRowId(ourType);
			module.Types.Add(ourType);
		}

		object GetModuleId() {
			var memoryStream = new MemoryStream();
			var writer = new BinaryWriter(memoryStream);
			if (module.Assembly != null)
				writer.Write(module.Assembly.FullName);
			writer.Write((module.Mvid ?? Guid.Empty).ToByteArray());
			var hash = new SHA1Managed().ComputeHash(memoryStream.GetBuffer());
			var guid = new Guid(BitConverter.ToInt32(hash, 0),
								BitConverter.ToInt16(hash, 4),
								BitConverter.ToInt16(hash, 6),
								hash[8], hash[9], hash[10], hash[11],
								hash[12], hash[13], hash[14], hash[15]);
			return guid.ToString("B");
		}

		TypeDef GetArrayType(long size) {
			CreateOurType();

			TypeDef arrayType;
			if (sizeToArrayType.TryGetValue(size, out arrayType))
				return arrayType;

			if (valueType == null)
				valueType = DotNetUtils.FindOrCreateTypeRef(module, module.CorLibTypes.AssemblyRef, "System", "ValueType", false);
			arrayType = new TypeDefUser("", string.Format("__StaticArrayInitTypeSize={0}", size), valueType.TypeDefOrRef);
			module.UpdateRowId(arrayType);
			arrayType.Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout |
							TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.AnsiClass;
			ourType.NestedTypes.Add(arrayType);
			sizeToArrayType[size] = arrayType;
			arrayType.ClassLayout = new ClassLayoutUser(1, (uint)size);
			return arrayType;
		}

		public FieldDef Create(byte[] data) {
			var arrayType = GetArrayType(data.LongLength);
			var fieldSig = new FieldSig(new ValueTypeSig(arrayType));
			var attrs = FieldAttributes.Assembly | FieldAttributes.Static;
			var field = new FieldDefUser(string.Format("field_{0}", unique++), fieldSig, attrs);
			module.UpdateRowId(field);
			field.HasFieldRVA = true;
			ourType.Fields.Add(field);
			var iv = new byte[data.Length];
			Array.Copy(data, iv, data.Length);
			field.InitialValue = iv;
			return field;
		}
	}
}
