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

namespace de4dot.code.deobfuscators.CodeFort {
	class ProxyCallFixer : ProxyCallFixer3 {
		MethodDefAndDeclaringTypeDict<bool> proxyTargetMethods = new MethodDefAndDeclaringTypeDict<bool>();
		TypeDef proxyMethodsType;

		public TypeDef ProxyMethodsType {
			get { return proxyMethodsType; }
		}

		public ProxyCallFixer(ModuleDefMD module)
			: base(module) {
		}

		public bool IsProxyTargetMethod(IMethod method) {
			return proxyTargetMethods.Find(method);
		}

		public void FindDelegateCreator() {
			foreach (var type in module.Types) {
				var creatorMethod = CheckType(type);
				if (creatorMethod == null)
					continue;

				SetDelegateCreatorMethod(creatorMethod);
				return;
			}
		}

		static MethodDef CheckType(TypeDef type) {
			if (type.Fields.Count != 1)
				return null;
			if (type.Fields[0].FieldSig.GetFieldType().GetFullName() != "System.Reflection.Module")
				return null;
			return CheckMethods(type);
		}

		static MethodDef CheckMethods(TypeDef type) {
			if (type.Methods.Count != 3)
				return null;

			MethodDef creatorMethod = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					continue;
				if (DotNetUtils.IsMethod(method, "System.Void", "(System.Int32)")) {
					creatorMethod = method;
					continue;
				}
				if (DotNetUtils.IsMethod(method, "System.MulticastDelegate", "(System.Type,System.Reflection.MethodInfo,System.Int32)"))
					continue;

				return null;
			}
			return creatorMethod;
		}

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			var instrs = cctor.Body.Instructions;
			if (instrs.Count != 3)
				return null;
			var ldci4 = instrs[0];
			if (!ldci4.IsLdcI4())
				return null;
			var call = instrs[1];
			if (call.OpCode.Code != Code.Call)
				return null;
			if (!IsDelegateCreatorMethod(call.Operand as MethodDef))
				return null;
			int rid = ldci4.GetLdcI4Value();
			if (cctor.DeclaringType.Rid != rid)
				throw new ApplicationException("Invalid rid");
			return rid;
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			uint rid = 0;
			foreach (var c in field.Name.String)
				rid = (rid << 4) + (uint)HexToInt((char)((byte)c + 0x2F));
			rid &= 0x00FFFFFF;
			calledMethod = module.ResolveMemberRef(rid);
			var calledMethodDef = DotNetUtils.GetMethod2(module, calledMethod);
			if (calledMethodDef != null) {
				proxyMethodsType = calledMethodDef.DeclaringType;
				proxyTargetMethods.Add(calledMethodDef, true);
				calledMethod = calledMethodDef;
			}
			callOpcode = OpCodes.Call;
		}

		static int HexToInt(char c) {
			if ('0' <= c && c <= '9')
				return c - '0';
			if ('a' <= c && c <= 'f')
				return c - 'a' + 10;
			if ('A' <= c && c <= 'F')
				return c - 'A' + 10;
			throw new ApplicationException("Invalid hex digit");
		}
	}
}
