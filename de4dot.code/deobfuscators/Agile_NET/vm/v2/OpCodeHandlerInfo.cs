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
using System.Text;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class OpCodeHandlerInfo {
		public HandlerTypeCode TypeCode { get; private set; }
		public string Name { get; private set; }

		public OpCodeHandlerInfo(HandlerTypeCode typeCode) {
			this.TypeCode = typeCode;
			this.Name = GetHandlerName(typeCode);
		}

		public override string ToString() {
			return Name;
		}

		public static string GetCompositeName(IList<HandlerTypeCode> typeCodes) {
			if (typeCodes.Count == 0)
				return "<nothing>";
			var sb = new StringBuilder();
			foreach (var typeCode in typeCodes) {
				if (sb.Length != 0)
					sb.Append(", ");
				sb.Append(GetHandlerName(typeCode));
			}
			return sb.ToString();
		}

		public static string GetHandlerName(HandlerTypeCode code) {
			switch (code) {
			case HandlerTypeCode.Add:			return "add";
			case HandlerTypeCode.Add_Ovf:		return "add.ovf";
			case HandlerTypeCode.Add_Ovf_Un:	return "add.ovf.un";
			case HandlerTypeCode.And:			return "and";
			case HandlerTypeCode.Beq:			return "beq";
			case HandlerTypeCode.Bge:			return "bge";
			case HandlerTypeCode.Bge_Un:		return "bge.un";
			case HandlerTypeCode.Bgt:			return "bgt";
			case HandlerTypeCode.Bgt_Un:		return "bgt.un";
			case HandlerTypeCode.Ble:			return "ble";
			case HandlerTypeCode.Ble_Un:		return "ble.un";
			case HandlerTypeCode.Blt:			return "blt";
			case HandlerTypeCode.Blt_Un:		return "blt.un";
			case HandlerTypeCode.Bne_Un:		return "bne.un";
			case HandlerTypeCode.Box:			return "box";
			case HandlerTypeCode.Br:			return "br";
			case HandlerTypeCode.Brfalse:		return "brfalse";
			case HandlerTypeCode.Brtrue:		return "brtrue";
			case HandlerTypeCode.Call:			return "call";
			case HandlerTypeCode.Callvirt:		return "callvirt";
			case HandlerTypeCode.Castclass:		return "castclass";
			case HandlerTypeCode.Ceq:			return "ceq";
			case HandlerTypeCode.Cgt:			return "cgt";
			case HandlerTypeCode.Cgt_Un:		return "cgt.un";
			case HandlerTypeCode.Clt:			return "clt";
			case HandlerTypeCode.Clt_Un:		return "clt.un";
			case HandlerTypeCode.Conv:			return "conv";
			case HandlerTypeCode.Div:			return "div";
			case HandlerTypeCode.Div_Un:		return "div.un";
			case HandlerTypeCode.Dup:			return "dup";
			case HandlerTypeCode.Endfinally:	return "endfinally";
			case HandlerTypeCode.Initobj:		return "initobj";
			case HandlerTypeCode.Isinst:		return "isinst";
			case HandlerTypeCode.Ldarg:			return "ldarg";
			case HandlerTypeCode.Ldarga:		return "ldarga";
			case HandlerTypeCode.Ldc:			return "ldc";
			case HandlerTypeCode.Ldelem:		return "ldelem";
			case HandlerTypeCode.Ldelema:		return "ldelema";
			case HandlerTypeCode.Ldfld_Ldsfld:	return "ldfld/ldsfld";
			case HandlerTypeCode.Ldflda_Ldsflda:return "ldflda/ldsflda";
			case HandlerTypeCode.Ldftn:			return "ldftn";
			case HandlerTypeCode.Ldlen:			return "ldlen";
			case HandlerTypeCode.Ldloc:			return "ldloc";
			case HandlerTypeCode.Ldloca:		return "ldloca";
			case HandlerTypeCode.Ldobj:			return "ldobj";
			case HandlerTypeCode.Ldstr:			return "ldstr";
			case HandlerTypeCode.Ldtoken:		return "ldtoken";
			case HandlerTypeCode.Ldvirtftn:		return "ldvirtftn";
			case HandlerTypeCode.Leave:			return "leave";
			case HandlerTypeCode.Mul:			return "mul";
			case HandlerTypeCode.Mul_Ovf:		return "mul.ovf";
			case HandlerTypeCode.Mul_Ovf_Un:	return "mul.ovf.un";
			case HandlerTypeCode.Neg:			return "neg";
			case HandlerTypeCode.Newarr:		return "newarr";
			case HandlerTypeCode.Newobj:		return "newobj";
			case HandlerTypeCode.Nop:			return "nop";
			case HandlerTypeCode.Not:			return "not";
			case HandlerTypeCode.Or:			return "or";
			case HandlerTypeCode.Pop:			return "pop";
			case HandlerTypeCode.Rem:			return "rem";
			case HandlerTypeCode.Rem_Un:		return "rem.un";
			case HandlerTypeCode.Ret:			return "ret";
			case HandlerTypeCode.Rethrow:		return "rethrow";
			case HandlerTypeCode.Shl:			return "shl";
			case HandlerTypeCode.Shr:			return "shr";
			case HandlerTypeCode.Shr_Un:		return "shr.un";
			case HandlerTypeCode.Starg:			return "starg";
			case HandlerTypeCode.Stelem:		return "stelem";
			case HandlerTypeCode.Stfld_Stsfld:	return "stfld/stsfld";
			case HandlerTypeCode.Stloc:			return "stloc";
			case HandlerTypeCode.Stobj:			return "stobj";
			case HandlerTypeCode.Sub:			return "sub";
			case HandlerTypeCode.Sub_Ovf:		return "sub.ovf";
			case HandlerTypeCode.Sub_Ovf_Un:	return "sub.ovf.un";
			case HandlerTypeCode.Switch:		return "switch";
			case HandlerTypeCode.Throw:			return "throw";
			case HandlerTypeCode.Unbox_Any:		return "unbox.any";
			case HandlerTypeCode.Xor:			return "xor";
			default: throw new ApplicationException("Invalid handler type code");
			}
		}
	}
}
