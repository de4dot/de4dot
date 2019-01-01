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

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	// These constants are hard coded. Don't change the values (i.e., only append if more are needed)
	enum HandlerTypeCode {
		Add,
		Add_Ovf,
		Add_Ovf_Un,
		And,
		Beq,
		Bge,
		Bge_Un,
		Bgt,
		Bgt_Un,
		Ble,
		Ble_Un,
		Blt,
		Blt_Un,
		Bne_Un,
		Box,
		Br,
		Brfalse,
		Brtrue,
		Call,
		Callvirt,
		Castclass,
		Ceq,
		Cgt,
		Cgt_Un,
		Clt,
		Clt_Un,
		Conv,
		Div,
		Div_Un,
		Dup,
		Endfinally,
		Initobj,
		Isinst,
		Ldarg,
		Ldarga,
		Ldc,
		Ldelem,
		Ldelema,
		Ldfld_Ldsfld,
		Ldflda_Ldsflda,
		Ldftn,
		Ldlen,
		Ldloc,
		Ldloca,
		Ldobj,
		Ldstr,
		Ldtoken,
		Ldvirtftn,
		Leave,
		Mul,
		Mul_Ovf,
		Mul_Ovf_Un,
		Neg,
		Newarr,
		Newobj,
		Nop,
		Not,
		Or,
		Pop,
		Rem,
		Rem_Un,
		Ret,
		Rethrow,
		Shl,
		Shr,
		Shr_Un,
		Starg,
		Stelem,
		Stfld_Stsfld,
		Stloc,
		Stobj,
		Sub,
		Sub_Ovf,
		Sub_Ovf_Un,
		Switch,
		Throw,
		Unbox_Any,
		Xor,
	}
}
