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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class ProxyCallFixer : ProxyCallFixer2 {
		public ProxyCallFixer(ModuleDefinition module)
			: base(module) {
		}

		class MyInfo {
			public MethodDefinition method;
			public DelegateInfo delegateInfo;
			public MyInfo(MethodDefinition method, DelegateInfo delegateInfo) {
				this.method = method;
				this.delegateInfo = delegateInfo;
			}
		}

		public new void find() {
			Log.v("Finding all proxy delegates");
			var infos = new List<MyInfo>();
			foreach (var type in module.GetTypes()) {
				if (type.BaseType == null || type.BaseType.FullName != "System.MulticastDelegate")
					continue;

				infos.Clear();
				foreach (var method in type.Methods) {
					DelegateInfo info;
					if (!checkProxyMethod(method, out info))
						continue;
					infos.Add(new MyInfo(method, info));
				}

				if (infos.Count == 0)
					continue;

				Log.v("Found proxy delegate: {0} ({1:X8})", Utils.removeNewlines(type), type.MetadataToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				Log.indent();
				foreach (var info in infos) {
					var di = info.delegateInfo;
					add(info.method, di);
					Log.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.removeNewlines(di.field.Name),
								di.callOpcode,
								Utils.removeNewlines(di.methodRef),
								di.methodRef.MetadataToken.ToUInt32());
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}
		}

		bool checkProxyMethod(MethodDefinition method, out DelegateInfo info) {
			info = null;
			if (!method.IsStatic || method.Body == null)
				return false;

			var instrs = method.Body.Instructions;
			if (instrs.Count < 7)
				return false;

			int index = 0;

			if (instrs[index].OpCode.Code != Code.Ldsfld)
				return false;
			var field = instrs[index++].Operand as FieldDefinition;
			if (field == null || !field.IsStatic)
				return false;
			if (!MemberReferenceHelper.compareTypes(method.DeclaringType, field.DeclaringType))
				return false;

			if (!DotNetUtils.isBrtrue(instrs[index++]))
				return false;
			if (instrs[index++].OpCode.Code != Code.Ldnull)
				return false;
			if (instrs[index].OpCode.Code != Code.Ldftn)
				return false;
			var calledMethod = instrs[index++].Operand as MethodReference;
			if (calledMethod == null)
				return false;
			if (instrs[index++].OpCode.Code != Code.Newobj)
				return false;
			if (instrs[index].OpCode.Code != Code.Stsfld)
				return false;
			if (!MemberReferenceHelper.compareFieldReference(field, instrs[index++].Operand as FieldReference))
				return false;
			if (instrs[index].OpCode.Code != Code.Ldsfld)
				return false;
			if (!MemberReferenceHelper.compareFieldReference(field, instrs[index++].Operand as FieldReference))
				return false;

			for (int i = 0; i < method.Parameters.Count; i++) {
				if (index >= instrs.Count)
					return false;
				if (DotNetUtils.getArgIndex(instrs[index++]) != i)
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

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			throw new System.NotImplementedException();
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			throw new System.NotImplementedException();
		}
	}
}
