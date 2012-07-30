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
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	class ProxyCallFixer : ProxyCallFixer2 {
		byte[] fileData;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDefinitionAndDeclaringTypeDict<ProxyCreatorInfo> methodToInfo = new MethodDefinitionAndDeclaringTypeDict<ProxyCreatorInfo>();
		FieldDefinitionAndDeclaringTypeDict<MethodDefinition> fieldToMethod = new FieldDefinitionAndDeclaringTypeDict<MethodDefinition>();
		x86Emulator x86emu;

		enum ProxyCreatorType {
			None,
			CallOrCallvirt,
			Newobj,
		}

		class ProxyCreatorInfo {
			public readonly MethodDefinition creatorMethod;
			public readonly ProxyCreatorType proxyCreatorType;
			public uint? key;
			public MethodDefinition nativeMethod;
			public ushort callvirtChar;

			public ProxyCreatorInfo(MethodDefinition creatorMethod, ProxyCreatorType proxyCreatorType) {
				this.creatorMethod = creatorMethod;
				this.proxyCreatorType = proxyCreatorType;
			}
		}

		protected override bool ProxyCallIsObfuscated {
			get { return true; }
		}

		public override IEnumerable<Tuple<MethodDefinition, string>> OtherMethods {
			get {
				var list = new List<Tuple<MethodDefinition, string>>();
				foreach (var info in methodToInfo.getValues()) {
					list.Add(new Tuple<MethodDefinition, string> {
						Item1 = info.creatorMethod,
						Item2 = "Delegate creator method",
					});
					list.Add(new Tuple<MethodDefinition, string> {
						Item1 = info.nativeMethod,
						Item2 = "Calculate RID native method",
					});
				}
				return list;
			}
		}

		public ProxyCallFixer(ModuleDefinition module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module) {
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			var instrs = cctor.Body.Instructions;
			object retVal = null;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldtoken = instrs[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;
				if (field == null)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (!isDelegateCreatorMethod(calledMethod))
					continue;

				fieldToMethod.add(field, calledMethod);
				retVal = this;
			}
			return retVal;
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			var info = getProxyCreatorInfo(field);
			var sig = module.GetSignatureBlob(field);
			int len = sig.Length;
			uint magic = (uint)((sig[len - 2] << 24) | (sig[len - 3] << 16) | (sig[len - 5] << 8) | sig[len - 6]);
			uint rid = getRid(info, magic);
			int token = (sig[len - 7] << 24) | (int)rid;
			calledMethod = module.LookupToken(token) as MethodReference;
			callOpcode = getCallOpCode(info, field);
		}

		OpCode getCallOpCode(ProxyCreatorInfo info, FieldDefinition field) {
			switch (info.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				if (field.Name.Length > 0 && field.Name[0] == info.callvirtChar)
					return OpCodes.Callvirt;
				return OpCodes.Call;

			case ProxyCreatorType.Newobj:
				return OpCodes.Newobj;

			default: throw new NotSupportedException();
			}
		}

		ProxyCreatorInfo getProxyCreatorInfo(FieldReference field) {
			return methodToInfo.find(fieldToMethod.find(field));
		}

		uint getRid(ProxyCreatorInfo info, uint magic) {
			if (info.key != null)
				return magic ^ info.key.Value;

			if (info.nativeMethod != null) {
				if (x86emu == null)
					x86emu = new x86Emulator(new PeImage(fileData));
				return x86emu.emulate((uint)info.nativeMethod.RVA, magic);
			}

			throw new NotImplementedException();
		}

		public void findDelegateCreator() {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.RuntimeFieldHandle)"))
					continue;
				var creatorType = getProxyCreatorType(method);
				if (creatorType == ProxyCreatorType.None)
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Reflection.Module::ResolveSignature(System.Int32)"))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)"))
					continue;

				methodToInfo.add(method, createProxyCreatorInfo(method, creatorType));
				setDelegateCreatorMethod(method);
			}
		}

		static ProxyCreatorType getProxyCreatorType(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as FieldReference;
				if (field == null)
					continue;
				switch (field.FullName) {
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Call":
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Callvirt":
					return ProxyCreatorType.CallOrCallvirt;

				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Newobj":
					return ProxyCreatorType.Newobj;
				}
			}
			return ProxyCreatorType.None;
		}

		ProxyCreatorInfo createProxyCreatorInfo(MethodDefinition creatorMethod, ProxyCreatorType proxyCreatorType) {
			simpleDeobfuscator.deobfuscate(creatorMethod, true);
			var info = new ProxyCreatorInfo(creatorMethod, proxyCreatorType);

			if (!initializeKey(info))
				throw new NotSupportedException("Couldn't find decryption key");

			if (info.proxyCreatorType == ProxyCreatorType.CallOrCallvirt) {
				if (!initializeCallvirtChar(info))
					throw new ApplicationException("Couldn't find callvirt char");
			}

			return info;
		}

		bool initializeKey(ProxyCreatorInfo info) {
			var instrs = info.creatorMethod.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (index < 0)
					break;

				uint key;
				if (getKey(instrs, index + 1, out key)) {
					info.key = key;
					return true;
				}

				var nativeMethod = getNativeMethod(instrs, index + 1);
				if (nativeMethod != null) {
					info.nativeMethod = nativeMethod;
					return true;
				}
			}
			return false;
		}

		static bool getKey(IList<Instruction> instrs, int index, out uint key) {
			key = 0;
			if (index + 2 >= instrs.Count)
				return false;
			if (!DotNetUtils.isLdloc(instrs[index++]))
				return false;
			var ldci4 = instrs[index++];
			if (!DotNetUtils.isLdcI4(ldci4))
				return false;
			if (instrs[index++].OpCode.Code != Code.Xor)
				return false;

			key = (uint)DotNetUtils.getLdcI4Value(ldci4);
			return true;
		}

		static MethodDefinition getNativeMethod(IList<Instruction> instrs, int index) {
			if (index + 1 >= instrs.Count)
				return null;
			if (!DotNetUtils.isLdloc(instrs[index++]))
				return null;
			var call = instrs[index++];
			if (call.OpCode.Code != Code.Call)
				return null;
			var calledMethod = call.Operand as MethodDefinition;
			if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
				return null;
			return calledMethod;
		}

		bool initializeCallvirtChar(ProxyCreatorInfo info) {
			var instrs = info.creatorMethod.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.Char System.String::get_Chars(System.Int32)");
				if (index < 0)
					break;

				index++;
				if (index >= instrs.Count)
					break;

				var ldci4 = instrs[index];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				info.callvirtChar = (ushort)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			return false;
		}
	}
}
