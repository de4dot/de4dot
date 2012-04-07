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

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	// Tries to restore the operands of the following CIL instructions:
	//	ldelema
	//	ldobj
	//	stobj
	class CilOperandInstructionRestorer {
		MethodDefinition method;

		public bool restore(MethodDefinition method) {
			this.method = method;
			bool atLeastOneFailed = false;

			if (method == null || method.Body == null)
				return !atLeastOneFailed;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.Operand != null)
					continue;

				TypeReference operandType = null;
				switch (instr.OpCode.Code) {
				case Code.Ldelema:
					var arrayType = MethodStack.getLoadedType(method, instrs, i, 1) as ArrayType;
					if (arrayType == null)
						break;
					operandType = arrayType.ElementType;
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

				instr.Operand = operandType;
			}

			return !atLeastOneFailed;
		}

		static TypeReference getPtrElementType(TypeReference type) {
			if (type == null)
				return null;
			var pt = type as PointerType;
			if (pt != null)
				return pt.ElementType;
			var bt = type as ByReferenceType;
			if (bt != null)
				return bt.ElementType;
			return null;
		}

		bool isValidType(TypeReference type) {
			if (type == null)
				return false;
			if (type.EType == ElementType.Void)
				return false;

			while (type != null) {
				switch (MemberReferenceHelper.getMemberReferenceType(type)) {
				case CecilType.ArrayType:
				case CecilType.GenericInstanceType:
				case CecilType.PointerType:
				case CecilType.TypeDefinition:
				case CecilType.TypeReference:
				case CecilType.FunctionPointerType:
					break;

				case CecilType.GenericParameter:
					var gp = (GenericParameter)type;
					if (method.DeclaringType != gp.Owner && method != gp.Owner)
						return false;
					break;

				case CecilType.ByReferenceType:
				case CecilType.OptionalModifierType:
				case CecilType.PinnedType:
				case CecilType.RequiredModifierType:
				case CecilType.SentinelType:
				default:
					return false;
				}

				if (!(type is TypeSpecification))
					break;
				type = ((TypeSpecification)type).ElementType;
			}

			return type != null;
		}
	}
}
