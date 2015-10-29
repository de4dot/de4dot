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
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ProxyCallFixer : ProxyCallFixer1 {
		MainType mainType;
		Info info = new Info();
		IBinaryReader reader;

		class Info {
			public TypeDef proxyType;
			public MethodDef initMethod;
			public FieldDef dataField;
			public TypeDef ilgeneratorType;
			public TypeDef fieldInfoType;
			public TypeDef methodInfoType;
		}

		class Context {
			public int offset;

			public Context(int offset) {
				this.offset = offset;
			}
		}

		public bool FoundProxyType {
			get { return info.proxyType != null; }
		}

		public bool CanRemoveTypes {
			get {
				return info.proxyType != null &&
					info.ilgeneratorType != null &&
					info.fieldInfoType != null &&
					info.methodInfoType != null;
			}
		}

		public TypeDef IlGeneratorType {
			get { return info.ilgeneratorType; }
		}

		public TypeDef FieldInfoType {
			get { return info.fieldInfoType; }
		}

		public TypeDef MethodInfoType {
			get { return info.methodInfoType; }
		}

		public ProxyCallFixer(ModuleDefMD module, MainType mainType)
			: base(module) {
			this.mainType = mainType;
		}

		public ProxyCallFixer(ModuleDefMD module, MainType mainType, ProxyCallFixer oldOne)
			: base(module, oldOne) {
			this.mainType = mainType;
			info.proxyType = Lookup(oldOne.info.proxyType, "Could not find proxyType");
			info.initMethod = Lookup(oldOne.info.initMethod, "Could not find initMethod");
			info.dataField = Lookup(oldOne.info.dataField, "Could not find dataField");
			info.ilgeneratorType = Lookup(oldOne.info.ilgeneratorType, "Could not find ilgeneratorType");
			info.fieldInfoType = Lookup(oldOne.info.fieldInfoType, "Could not find fieldInfoType");
			info.methodInfoType = Lookup(oldOne.info.methodInfoType, "Could not find methodInfoType");
		}

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				if (call.Operand != info.initMethod)
					continue;

				int offset = ldci4.GetLdcI4Value();
				reader.Position = offset;
				uint rid = reader.ReadCompressedUInt32();
				if (rid != type.Rid)
					throw new ApplicationException("Invalid RID");
				return string.Empty;	// It's non-null
			}
			return null;
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			byte flags = reader.ReadByte();

			int methodToken = 0x06000000 + ((flags & 0x3F) << 24) + (int)reader.ReadCompressedUInt32();
			int genericTypeToken = (flags & 0x40) == 0 ? -1 : 0x1B000000 + (int)reader.ReadCompressedUInt32();
			callOpcode = (flags & 0x80) != 0 ? OpCodes.Callvirt : OpCodes.Call;

			calledMethod = module.ResolveToken(methodToken) as IMethod;
			if (calledMethod == null)
				throw new ApplicationException("Could not find method");
			if (genericTypeToken != -1 && calledMethod.DeclaringType.MDToken.ToInt32() != genericTypeToken)
				throw new ApplicationException("Invalid declaring type token");
		}

		public void FindDelegateCreator() {
			if (!mainType.Detected)
				return;

			var infoTmp = new Info();
			if (!InitializeInfo(infoTmp, mainType.Type))
				return;

			info = infoTmp;
			SetDelegateCreatorMethod(info.initMethod);
		}

		bool InitializeInfo(Info infoTmp, TypeDef type) {
			foreach (var dtype in type.NestedTypes) {
				var cctor = dtype.FindMethod(".cctor");
				if (cctor == null)
					continue;
				if (!InitProxyType(infoTmp, cctor))
					continue;

				return true;
			}

			return false;
		}

		bool InitProxyType(Info infoTmp, MethodDef method) {
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
				if (!calledMethod.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "(System.Int32)"))
					continue;
				if (!CheckProxyType(infoTmp, calledMethod.DeclaringType))
					continue;

				infoTmp.proxyType = calledMethod.DeclaringType;
				infoTmp.initMethod = calledMethod;
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
		bool CheckProxyType(Info infoTmp, TypeDef type) {
			if (type.NestedTypes.Count != 1)
				return false;

			if (!new FieldTypes(type).All(requiredFields))
				return false;

			var fields = GetRvaFields(type);
			if (fields.Count != 1)
				return false;
			var field = fields[0];
			var fieldType = DotNetUtils.GetType(module, field.FieldSig.GetFieldType());
			if (type.NestedTypes.IndexOf(fieldType) < 0)
				return false;
			if (field.InitialValue == null || field.InitialValue.Length == 0)
				return false;

			infoTmp.dataField = field;
			return true;
		}

		static List<FieldDef> GetRvaFields(TypeDef type) {
			var fields = new List<FieldDef>();
			foreach (var field in type.Fields) {
				if (field.RVA != 0)
					fields.Add(field);
			}
			return fields;
		}

		protected override IEnumerable<TypeDef> GetDelegateTypes() {
			if (!mainType.Detected)
				return new List<TypeDef>();
			return mainType.Type.NestedTypes;
		}

		public void Initialize() {
			if (info.dataField == null)
				return;

			FindOtherTypes();

			var decompressed = DeobUtils.Inflate(info.dataField.InitialValue, true);
			reader = MemoryImageStream.Create(decompressed);
			info.dataField.FieldSig.Type = module.CorLibTypes.Byte;
			info.dataField.InitialValue = new byte[1];
			info.dataField.RVA = 0;
		}

		void FindOtherTypes() {
			if (info.proxyType == null)
				return;

			foreach (var method in info.proxyType.Methods) {
				var sig = method.MethodSig;
				if (sig == null || sig.Params.Count != 4)
					continue;

				if (sig.Params[2].GetFullName() != "System.Type[]")
					continue;
				var methodType = sig.Params[0].TryGetTypeDef();
				var fieldType = sig.Params[1].TryGetTypeDef();
				var ilgType = sig.Params[3].TryGetTypeDef();
				if (!CheckMethodType(methodType))
					continue;
				if (!CheckFieldType(fieldType))
					continue;
				if (!CheckIlGeneratorType(ilgType))
					continue;

				info.ilgeneratorType = ilgType;
				info.methodInfoType = methodType;
				info.fieldInfoType = fieldType;
			}
		}

		bool CheckMethodType(TypeDef type) {
			if (type == null || type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if (type.Fields.Count != 1)
				return false;
			if (DotNetUtils.GetField(type, "System.Reflection.MethodInfo") == null)
				return false;

			return true;
		}

		bool CheckFieldType(TypeDef type) {
			if (type == null || type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if (DotNetUtils.GetField(type, "System.Reflection.FieldInfo") == null)
				return false;

			return true;
		}

		bool CheckIlGeneratorType(TypeDef type) {
			if (type == null || type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if (type.Fields.Count != 1)
				return false;
			if (DotNetUtils.GetField(type, "System.Reflection.Emit.ILGenerator") == null)
				return false;

			return true;
		}
	}
}
