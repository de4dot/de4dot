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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ProxyDelegateFinder : ProxyDelegateFinderBase {
		Info info = new Info();
		BinaryReader reader;

		class Info {
			public TypeDefinition mainType;
			public TypeDefinition proxyType;
			public MethodDefinition initMethod;
			public FieldDefinition dataField;
		}

		class Context {
			public int offset;

			public Context(int offset) {
				this.offset = offset;
			}
		}

		public ProxyDelegateFinder(ModuleDefinition module)
			: base(module) {
		}

		public ProxyDelegateFinder(ModuleDefinition module, ProxyDelegateFinder oldOne)
			: base(module, oldOne) {
			info.mainType = lookup(oldOne.info.mainType, "Could not find mainType");
			info.proxyType = lookup(oldOne.info.proxyType, "Could not find proxyType");
			info.initMethod = lookup(oldOne.info.initMethod, "Could not find initMethod");
			info.dataField = lookup(oldOne.info.dataField, "Could not find dataField");
		}

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				if (call.Operand != info.initMethod)
					continue;

				int offset = DotNetUtils.getLdcI4Value(ldci4);
				reader.BaseStream.Position = offset;
				int rid = DeobUtils.readVariableLengthInt32(reader);
				if (rid != type.MetadataToken.RID)
					throw new ApplicationException("Invalid RID");
				return string.Empty;	// It's non-null
			}
			return null;
		}

		protected override void onFoundProxyDelegate(TypeDefinition type) {
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			byte flags = reader.ReadByte();

			int methodToken = 0x06000000 + ((flags & 0x3F) << 24) + DeobUtils.readVariableLengthInt32(reader);
			int genericTypeToken = (flags & 0x40) == 0 ? -1 : 0x1B000000 + DeobUtils.readVariableLengthInt32(reader);
			callOpcode = (flags & 0x80) != 0 ? OpCodes.Callvirt : OpCodes.Call;

			calledMethod = module.LookupToken(methodToken) as MethodReference;
			if (calledMethod == null)
				throw new ApplicationException("Could not find method");
			if (genericTypeToken != -1 && calledMethod.DeclaringType.MetadataToken.ToInt32() != genericTypeToken)
				throw new ApplicationException("Invalid declaring type token");
		}

		public void findDelegateCreator() {
			var moduleCctor = DotNetUtils.getModuleTypeCctor(module);
			if (moduleCctor == null)
				return;
			foreach (var call in DotNetUtils.getCalledMethods(module, moduleCctor)) {
				if (!call.Item2.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(call.Item2, "System.Void", "(System.Boolean,System.Boolean)"))
					continue;
				var infoTmp = new Info();
				if (!initializeInfo(infoTmp, call.Item1))
					continue;

				info = infoTmp;
				setDelegateCreatorMethod(info.initMethod);
				return;
			}
		}

		bool initializeInfo(Info infoTmp, TypeDefinition type) {
			foreach (var dtype in type.NestedTypes) {
				var cctor = DotNetUtils.getMethod(dtype, ".cctor");
				if (cctor == null)
					continue;
				if (!initProxyType(infoTmp, cctor))
					continue;

				infoTmp.mainType = type;
				return true;
			}

			return false;
		}

		bool initProxyType(Info infoTmp, MethodDefinition method) {
			foreach (var call in DotNetUtils.getCalledMethods(module, method)) {
				if (!call.Item2.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(call.Item2, "System.Void", "(System.Int32)"))
					continue;
				if (!checkProxyType(infoTmp, call.Item1))
					continue;

				infoTmp.proxyType = call.Item1;
				infoTmp.initMethod = call.Item2;
				return true;
			}
			return false;
		}

		static string[] requiredFields = new string[] {
			"System.Byte[]",
			"System.Int32",
			"System.ModuleHandle",
			"System.Reflection.Emit.OpCode",
			"System.Reflection.Emit.OpCode[]",
		};
		bool checkProxyType(Info infoTmp, TypeDefinition type) {
			if (type.NestedTypes.Count != 1)
				return false;

			if (!new FieldTypes(type).all(requiredFields))
				return false;

			var fields = getRvaFields(type);
			if (fields.Count != 1)
				return false;
			var field = fields[0];
			var fieldType = DotNetUtils.getType(module, field.FieldType);
			if (type.NestedTypes.IndexOf(fieldType) < 0)
				return false;
			if (field.InitialValue == null || field.InitialValue.Length == 0)
				return false;

			infoTmp.dataField = field;
			return true;
		}

		static List<FieldDefinition> getRvaFields(TypeDefinition type) {
			var fields = new List<FieldDefinition>();
			foreach (var field in type.Fields) {
				if (field.RVA != 0)
					fields.Add(field);
			}
			return fields;
		}

		protected override IEnumerable<TypeDefinition> getDelegateTypes() {
			return info.mainType.NestedTypes;
		}

		public void initialize() {
			if (info.dataField == null)
				return;

			var decompressed = DeobUtils.inflate(info.dataField.InitialValue, true);
			reader = new BinaryReader(new MemoryStream(decompressed));
		}
	}
}
