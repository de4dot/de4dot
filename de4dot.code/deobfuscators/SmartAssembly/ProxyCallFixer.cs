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

namespace de4dot.code.deobfuscators.SmartAssembly {
	class ProxyCallFixer : ProxyCallFixer1 {
		static readonly Dictionary<char, int> specialCharsDict = new Dictionary<char, int>();
		static readonly char[] specialChars = new char[] {
			'\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07', '\x08',
			'\x0E', '\x0F', '\x10', '\x11', '\x12', '\x13', '\x14', '\x15',
			'\x16', '\x17', '\x18', '\x19', '\x1A', '\x1B', '\x1C', '\x1D',
			'\x1E', '\x1F', '\x7F', '\x80', '\x81', '\x82', '\x83', '\x84',
			'\x86', '\x87', '\x88', '\x89', '\x8A', '\x8B', '\x8C', '\x8D',
			'\x8E', '\x8F', '\x90', '\x91', '\x92', '\x93', '\x94', '\x95',
			'\x96', '\x97', '\x98', '\x99', '\x9A', '\x9B', '\x9C', '\x9D',
			'\x9E', '\x9F',
		};

		ISimpleDeobfuscator simpleDeobfuscator;

		static ProxyCallFixer() {
			for (int i = 0; i < specialChars.Length; i++)
				specialCharsDict[specialChars[i]] = i;
		}

		public ProxyCallFixer(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module) {
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			var instrs = cctor.Body.Instructions;
			if (instrs.Count > 10)
				return null;
			if (instrs.Count != 3)
				simpleDeobfuscator.Deobfuscate(cctor);
			if (instrs.Count != 3)
				return null;
			if (!instrs[0].IsLdcI4())
				return null;
			if (instrs[1].OpCode != OpCodes.Call || !IsDelegateCreatorMethod(instrs[1].Operand as MethodDef))
				return null;
			if (instrs[2].OpCode != OpCodes.Ret)
				return null;

			int delegateToken = 0x02000001 + instrs[0].GetLdcI4Value();
			if (type.MDToken.ToInt32() != delegateToken) {
				Logger.w("Delegate token is not current type");
				return null;
			}

			return new object();
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			callOpcode = OpCodes.Call;
			string name = field.Name.String;

			uint memberRefRid = 0;
			for (int i = name.Length - 1; i >= 0; i--) {
				char c = name[i];
				if (c == '~') {
					callOpcode = OpCodes.Callvirt;
					break;
				}

				int val;
				if (specialCharsDict.TryGetValue(c, out val))
					memberRefRid = memberRefRid * (uint)specialChars.Length + (uint)val;
			}
			memberRefRid++;

			calledMethod = module.ResolveMemberRef(memberRefRid);
			if (calledMethod == null)
				Logger.w("Ignoring invalid method RID: {0:X8}, field: {1:X8}", memberRefRid, field.MDToken.ToInt32());
		}

		public void FindDelegateCreator(ModuleDefMD module) {
			var callCounter = new CallCounter();
			foreach (var type in module.Types) {
				if (type.Namespace != "" || !DotNetUtils.DerivesFromDelegate(type))
					continue;
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;
				foreach (var method in DotNetUtils.GetMethodCalls(cctor))
					callCounter.Add(method);
			}

			var mostCalls = callCounter.Most();
			if (mostCalls == null)
				return;

			SetDelegateCreatorMethod(DotNetUtils.GetMethod(module, mostCalls));
		}
	}
}
