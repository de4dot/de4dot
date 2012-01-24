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
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class InitializedDataCreator {
		ModuleDefinition module;
		Dictionary<long, TypeDefinition> sizeToArrayType = new Dictionary<long, TypeDefinition>();
		TypeDefinition ourType;
		TypeReference valueType;
		int unique = 0;
		MethodReference initializeArrayMethod;

		public MethodReference InitializeArrayMethod {
			get { return createInitializeArrayMethod(); }
		}

		public InitializedDataCreator(ModuleDefinition module) {
			this.module = module;
		}

		MethodReference createInitializeArrayMethod() {
			if (initializeArrayMethod == null) {
				var runtimeHelpersType = DotNetUtils.findOrCreateTypeReference(module, module.TypeSystem.Corlib as AssemblyNameReference, "System.Runtime.CompilerServices", "RuntimeHelpers", false);
				initializeArrayMethod = new MethodReference("InitializeArray", module.TypeSystem.Void, runtimeHelpersType);
				var systemArrayType = DotNetUtils.findOrCreateTypeReference(module, module.TypeSystem.Corlib as AssemblyNameReference, "System", "Array", false);
				var runtimeFieldHandleType = DotNetUtils.findOrCreateTypeReference(module, module.TypeSystem.Corlib as AssemblyNameReference, "System", "RuntimeFieldHandle", true);
				initializeArrayMethod.Parameters.Add(new ParameterDefinition(systemArrayType));
				initializeArrayMethod.Parameters.Add(new ParameterDefinition(runtimeFieldHandleType));
			}
			return initializeArrayMethod;
		}

		public void addInitializeArrayCode(Block block, int start, int numToRemove, TypeReference elementType, byte[] data) {
			int index = start;
			block.replace(index++, numToRemove, DotNetUtils.createLdci4(data.Length / elementType.PrimitiveSize));
			block.insert(index++, Instruction.Create(OpCodes.Newarr, elementType));
			block.insert(index++, Instruction.Create(OpCodes.Dup));
			block.insert(index++, Instruction.Create(OpCodes.Ldtoken, create(data)));
			block.insert(index++, Instruction.Create(OpCodes.Call, InitializeArrayMethod));
		}

		void createOurType() {
			if (ourType != null)
				return;

			var attrs = TypeAttributes.NotPublic | TypeAttributes.AutoLayout |
						TypeAttributes.Class | TypeAttributes.AnsiClass;
			ourType = new TypeDefinition("", string.Format("<PrivateImplementationDetails>{0}", getModuleId()), attrs, module.TypeSystem.Object);
			ourType.MetadataToken = DotNetUtils.nextTypeDefToken();
			module.Types.Add(ourType);
		}

		object getModuleId() {
			var memoryStream = new MemoryStream();
			var writer = new BinaryWriter(memoryStream);
			if (module.Assembly != null)
				writer.Write(module.Assembly.Name.FullName);
			writer.Write(module.Mvid.ToByteArray());
			var hash = new SHA1Managed().ComputeHash(memoryStream.GetBuffer());
			var guid = new Guid(BitConverter.ToInt32(hash, 0),
								BitConverter.ToInt16(hash, 4),
								BitConverter.ToInt16(hash, 6),
								hash[8], hash[9], hash[10], hash[11],
								hash[12], hash[13], hash[14], hash[15]);
			return guid.ToString("B");
		}

		TypeDefinition getArrayType(long size) {
			createOurType();

			TypeDefinition arrayType;
			if (sizeToArrayType.TryGetValue(size, out arrayType))
				return arrayType;

			if (valueType == null)
				valueType = DotNetUtils.findOrCreateTypeReference(module, module.TypeSystem.Corlib as AssemblyNameReference, "System", "ValueType", false);
			var attrs = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout |
						TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.AnsiClass;
			arrayType = new TypeDefinition("", string.Format("__StaticArrayInitTypeSize={0}", size), attrs, valueType);
			arrayType.MetadataToken = DotNetUtils.nextTypeDefToken();
			ourType.NestedTypes.Add(arrayType);
			sizeToArrayType[size] = arrayType;
			arrayType.ClassSize = (int)size;
			arrayType.PackingSize = 1;
			arrayType.IsValueType = true;
			return arrayType;
		}

		public FieldDefinition create(byte[] data) {
			var arrayType = getArrayType(data.LongLength);
			var attrs = FieldAttributes.Assembly | FieldAttributes.Static;
			var field = new FieldDefinition(string.Format("field_{0}", unique++), attrs, arrayType);
			field.Attributes |= FieldAttributes.HasFieldRVA;
			field.MetadataToken = DotNetUtils.nextFieldToken();
			ourType.Fields.Add(field);
			var iv = new byte[data.Length];
			Array.Copy(data, iv, data.Length);
			field.InitialValue = iv;
			return field;
		}
	}
}
