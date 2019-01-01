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

using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	// Tries to restore the operands of the following CIL instructions:
	//	ldelema, ldelem.*, stelem.*, ldobj, stobj
	class CilOperandInstructionRestorer {
		MethodDef method;

		public bool Restore(MethodDef method) {
			this.method = method;
			bool atLeastOneFailed = false;

			if (method == null || method.Body == null)
				return !atLeastOneFailed;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.Operand != null)
					continue;

				TypeSig operandType = null, operandTypeTmp;
				OpCode newOpCode = null;
				SZArraySig arrayType;
				switch (instr.OpCode.Code) {
				case Code.Ldelem:
					arrayType = MethodStack.GetLoadedType(method, instrs, i, 1) as SZArraySig;
					if (arrayType == null)
						break;
					operandTypeTmp = arrayType.Next;
					if (operandTypeTmp == null)
						newOpCode = OpCodes.Ldelem_Ref;
					else {
						switch (operandTypeTmp.ElementType) {
						case ElementType.Boolean: newOpCode = OpCodes.Ldelem_I1; break;
						case ElementType.Char: newOpCode = OpCodes.Ldelem_U2; break;
						case ElementType.I:  newOpCode = OpCodes.Ldelem_I; break;
						case ElementType.I1: newOpCode = OpCodes.Ldelem_I1; break;
						case ElementType.I2: newOpCode = OpCodes.Ldelem_I2; break;
						case ElementType.I4: newOpCode = OpCodes.Ldelem_I4; break;
						case ElementType.I8: newOpCode = OpCodes.Ldelem_I8; break;
						case ElementType.U:  newOpCode = OpCodes.Ldelem_I; break;
						case ElementType.U1: newOpCode = OpCodes.Ldelem_U1; break;
						case ElementType.U2: newOpCode = OpCodes.Ldelem_U2; break;
						case ElementType.U4: newOpCode = OpCodes.Ldelem_U4; break;
						case ElementType.U8: newOpCode = OpCodes.Ldelem_I8; break;
						case ElementType.R4: newOpCode = OpCodes.Ldelem_R4; break;
						case ElementType.R8: newOpCode = OpCodes.Ldelem_R8; break;
						default:             newOpCode = OpCodes.Ldelem_Ref; break;
						//TODO: Ldelem
						}
					}
					break;

				case Code.Stelem:
					arrayType = MethodStack.GetLoadedType(method, instrs, i, 2) as SZArraySig;
					if (arrayType == null)
						break;
					operandTypeTmp = arrayType.Next;
					if (operandTypeTmp == null)
						newOpCode = OpCodes.Stelem_Ref;
					else {
						switch (operandTypeTmp.ElementType) {
						case ElementType.U:
						case ElementType.I:  newOpCode = OpCodes.Stelem_I; break;
						case ElementType.Boolean:
						case ElementType.U1:
						case ElementType.I1: newOpCode = OpCodes.Stelem_I1; break;
						case ElementType.Char:
						case ElementType.U2:
						case ElementType.I2: newOpCode = OpCodes.Stelem_I2; break;
						case ElementType.U4:
						case ElementType.I4: newOpCode = OpCodes.Stelem_I4; break;
						case ElementType.U8:
						case ElementType.I8: newOpCode = OpCodes.Stelem_I8; break;
						case ElementType.R4: newOpCode = OpCodes.Stelem_R4; break;
						case ElementType.R8: newOpCode = OpCodes.Stelem_R8; break;
						default: newOpCode = OpCodes.Stelem_Ref; break;
						//TODO: Stelem
						}
					}
					break;

				case Code.Ldelema:
					arrayType = MethodStack.GetLoadedType(method, instrs, i, 1) as SZArraySig;
					if (arrayType == null)
						break;
					operandType = arrayType.Next;
					break;

				case Code.Ldobj:
					operandType = GetPtrElementType(MethodStack.GetLoadedType(method, instrs, i, 0));
					break;

				case Code.Stobj:
					operandType = MethodStack.GetLoadedType(method, instrs, i, 0);
					if (!IsValidType(operandType))
						operandType = GetPtrElementType(MethodStack.GetLoadedType(method, instrs, i, 1));
					break;

				default:
					continue;
				}
				if (newOpCode == null && !IsValidType(operandType)) {
					atLeastOneFailed = true;
					continue;
				}

				instr.Operand = operandType.ToTypeDefOrRef();
				if (newOpCode != null)
					instr.OpCode = newOpCode;
			}

			return !atLeastOneFailed;
		}

		static TypeSig GetPtrElementType(TypeSig type) {
			if (type == null)
				return null;
			if (type.IsPointer || type.IsByRef)
				return type.Next;
			return null;
		}

		bool IsValidType(TypeSig type) {
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
