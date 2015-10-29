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

namespace de4dot.code.deobfuscators.Goliath_NET {
	class ProxyCallFixer : ProxyCallFixer2 {
		public ProxyCallFixer(ModuleDefMD module)
			: base(module) {
		}

		class MyInfo {
			public MethodDef method;
			public DelegateInfo delegateInfo;
			public MyInfo(MethodDef method, DelegateInfo delegateInfo) {
				this.method = method;
				this.delegateInfo = delegateInfo;
			}
		}

		public new void Find() {
			Logger.v("Finding all proxy delegates");
			var infos = new List<MyInfo>();
			foreach (var type in module.GetTypes()) {
				if (type.BaseType == null || type.BaseType.FullName != "System.MulticastDelegate")
					continue;

				infos.Clear();
				foreach (var method in type.Methods) {
					DelegateInfo info;
					if (!CheckProxyMethod(method, out info))
						continue;
					infos.Add(new MyInfo(method, info));
				}

				if (infos.Count == 0)
					continue;

				Logger.v("Found proxy delegate: {0} ({1:X8})", Utils.RemoveNewlines(type), type.MDToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				Logger.Instance.Indent();
				foreach (var info in infos) {
					var di = info.delegateInfo;
					Add(info.method, di);
					Logger.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.RemoveNewlines(di.field.Name),
								di.callOpcode,
								Utils.RemoveNewlines(di.methodRef),
								di.methodRef.MDToken.ToUInt32());
				}
				Logger.Instance.DeIndent();
				delegateTypesDict[type] = true;
			}
		}

		bool CheckProxyMethod(MethodDef method, out DelegateInfo info) {
			info = null;
			if (!method.IsStatic || method.Body == null)
				return false;

			var instrs = method.Body.Instructions;
			if (instrs.Count < 7)
				return false;

			int index = 0;

			if (instrs[index].OpCode.Code != Code.Ldsfld)
				return false;
			var field = instrs[index++].Operand as FieldDef;
			if (field == null || !field.IsStatic)
				return false;
			if (!new SigComparer().Equals(method.DeclaringType, field.DeclaringType))
				return false;

			if (!instrs[index++].IsBrtrue())
				return false;
			if (instrs[index++].OpCode.Code != Code.Ldnull)
				return false;
			if (instrs[index].OpCode.Code != Code.Ldftn)
				return false;
			var calledMethod = instrs[index++].Operand as IMethod;
			if (calledMethod == null)
				return false;
			if (instrs[index++].OpCode.Code != Code.Newobj)
				return false;
			if (instrs[index].OpCode.Code != Code.Stsfld)
				return false;
			if (!new SigComparer().Equals(field, instrs[index++].Operand as IField))
				return false;
			if (instrs[index].OpCode.Code != Code.Ldsfld)
				return false;
			if (!new SigComparer().Equals(field, instrs[index++].Operand as IField))
				return false;

			var sig = method.MethodSig;
			if (sig == null)
				return false;
			for (int i = 0; i < sig.Params.Count; i++) {
				if (index >= instrs.Count)
					return false;
				if (instrs[index++].GetParameterIndex() != i)
					return false;
			}

			if (index + 2 > instrs.Count)
				return false;
			var call = instrs[index++];
			if (call.OpCode.Code != Code.Callvirt)
				return false;

			if (instrs[index++].OpCode.Code != Code.Ret)
				return false;

			info = new DelegateInfo(field, calledMethod, OpCodes.Call);
			return true;
		}

		protected override object CheckCctor(TypeDef type, MethodDef cctor) {
			throw new System.NotImplementedException();
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			throw new System.NotImplementedException();
		}
	}
}
