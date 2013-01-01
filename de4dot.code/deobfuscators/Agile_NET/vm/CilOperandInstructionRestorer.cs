/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	// Tries to restore the operands of the following CIL instructions:
	//	ldelema
	//	ldobj
	//	stobj
	class CilOperandInstructionRestorer {
		MethodDef method;

		public bool restore(MethodDef method) {
			this.method = method;
			bool atLeastOneFailed = false;

			if (method == null || method.Body == null)
				return !atLeastOneFailed;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.Operand != null)
					continue;

				TypeSig operandType = null;
				switch (instr.OpCode.Code) {
				case Code.Ldelema:
					var arrayType = MethodStack.getLoadedType(method, instrs, i, 1) as SZArraySig;
					if (arrayType == null)
						break;
					operandType = arrayType.Next;
					break;

				case Code.Ldobj:
					operandType = getPtrElementType(MethodStack.getLoadedType(method, instrs, i, 0));
					break;

				case Code.Stobj:
					operandType = MethodStack.getLoadedType(method, instrs, i, 0);
					if (!isValidType(operandType))
						operandType = getPtrElementType(MethodStack.getLoadedType(method, instrs, i, 1));
					break;

				default:
					continue;
				}
				if (!isValidType(operandType)) {
					atLeastOneFailed = true;
					continue;
				}

				instr.Operand = operandType.ToTypeDefOrRef();
			}

			return !atLeastOneFailed;
		}

		static TypeSig getPtrElementType(TypeSig type) {
			if (type == null)
				return null;
			if (type.IsPointer || type.IsByRef)
				return type.Next;
			return null;
		}

		bool isValidType(TypeSig type) {
			type = type.RemovePinnedAndModifiers();
			if (type == null)
				return false;
			if (type.ElementType == ElementType.Void)
				return false;

			while (type != null) {
				switch (type.ElementType) {
				case ElementType.SZArray:
				case ElementType.Array:
				case ElementType.GenericInst:
				case ElementType.Ptr:
				case ElementType.Class:
				case ElementType.ValueType:
				case ElementType.FnPtr:
				case ElementType.Void:
				case ElementType.Boolean:
				case ElementType.Char:
				case ElementType.I1:
				case ElementType.U1:
				case ElementType.I2:
				case ElementType.U2:
				case ElementType.I4:
				case ElementType.U4:
				case ElementType.I8:
				case ElementType.U8:
				case ElementType.R4:
				case ElementType.R8:
				case ElementType.TypedByRef:
				case ElementType.I:
				case ElementType.U:
				case ElementType.String:
				case ElementType.Object:
					break;

				case ElementType.MVar:
					var gmvar = (GenericMVar)type;
					if (gmvar.Number >= method.MethodSig.GetGenParamCount())
						return false;
					break;

				case ElementType.Var:
					var gvar = (GenericVar)type;
					var dt = method.DeclaringType;
					if (dt == null || gvar.Number >= dt.GenericParameters.Count)
						return false;
					break;

				case ElementType.ByRef:
				case ElementType.CModOpt:
				case ElementType.CModReqd:
				case ElementType.Pinned:
				case ElementType.Sentinel:
				case ElementType.ValueArray:
				case ElementType.R:
				case ElementType.End:
				case ElementType.Internal:
				case ElementType.Module:
				default:
					return false;
				}
				if (type.Next == null)
					break;
				type = type.Next;
			}

			return type != null;
		}
	}
}
