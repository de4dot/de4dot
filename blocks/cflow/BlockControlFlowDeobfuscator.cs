/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	class BlockControlFlowDeobfuscator {
		Block block;
		InstructionEmulator instructionEmulator = new InstructionEmulator();

		public void init(Block block, IList<ParameterDefinition> args, IList<VariableDefinition> locals) {
			this.block = block;
			instructionEmulator.init(false, args, locals);
		}

		// Returns true if code was updated, false otherwise
		public bool deobfuscate() {
			var instructions = block.Instructions;
			if (instructions.Count == 0)
				return false;
			for (int i = 0; i < instructions.Count - 1; i++) {
				instructionEmulator.emulate(instructions[i].Instruction);
			}

			switch (block.LastInstr.OpCode.Code) {
			case Code.Beq:
			case Code.Beq_S:	return emulate_Beq();
			case Code.Bge:
			case Code.Bge_S:	return emulate_Bge();
			case Code.Bge_Un:
			case Code.Bge_Un_S:	return emulate_Bge_Un();
			case Code.Bgt:
			case Code.Bgt_S:	return emulate_Bgt();
			case Code.Bgt_Un:
			case Code.Bgt_Un_S:	return emulate_Bgt_Un();
			case Code.Ble:
			case Code.Ble_S:	return emulate_Ble();
			case Code.Ble_Un:
			case Code.Ble_Un_S:	return emulate_Ble_Un();
			case Code.Blt:
			case Code.Blt_S:	return emulate_Blt();
			case Code.Blt_Un:
			case Code.Blt_Un_S:	return emulate_Blt_Un();
			case Code.Bne_Un:
			case Code.Bne_Un_S:	return emulate_Bne_Un();
			case Code.Brfalse:
			case Code.Brfalse_S:return emulate_Brfalse();
			case Code.Brtrue:
			case Code.Brtrue_S:	return emulate_Brtrue();

			default:
				return false;
			}
		}

		bool emulateBranch(int stackArgs, bool isTaken) {
			// Pop the arguments to the bcc instruction. The dead code remover will get rid of the
			// pop and any pushed arguments. Insert the pops just before the bcc instr.
			for (int i = 0; i < stackArgs; i++)
				block.insert(block.Instructions.Count - 1, Instruction.Create(OpCodes.Pop));

			block.replaceBccWithBranch(isTaken);
			return true;
		}

		bool emulate_Beq() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: If it's an unknown int32/64, push 1 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, int1.value == int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, long1.value == long2.value);
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				return emulateBranch(2, real1.value == real2.value);
			}
			else if (val1.valueType == ValueType.Null && val2.valueType == ValueType.Null) {
				return emulateBranch(2, true);
			}
			else {
				return false;
			}
		}

		bool emulate_Bne_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: If it's an unknown int32/64, push 1 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, (uint)int1.value != (uint)int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, (ulong)long1.value != (ulong)long2.value);
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				return emulateBranch(2, real1.value != real2.value);
			}
			else if (val1.valueType == ValueType.Null && val2.valueType == ValueType.Null) {
				return emulateBranch(2, false);
			}
			else {
				return false;
			}
		}

		bool emulate_Bge() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, int1.value >= int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, long1.value >= long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MaxValue)
					return emulateBranch(2, true);	// max >= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MinValue)
					return emulateBranch(2, true);	// x >= min => true
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MaxValue)
					return emulateBranch(2, true);	// max >= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MinValue)
					return emulateBranch(2, true);	// x >= min => true
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Bge_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, (uint)int1.value >= (uint)int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, (ulong)long1.value >= (ulong)long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MaxValue)
					return emulateBranch(2, true);	// max >= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MinValue)
					return emulateBranch(2, true);	// x >= min => true
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MaxValue)
					return emulateBranch(2, true);	// max >= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MinValue)
					return emulateBranch(2, true);	// x >= min => true
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Bgt() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, int1.value > int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, long1.value > long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MinValue)
					return emulateBranch(2, false);	// min > x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MaxValue)
					return emulateBranch(2, false);	// x > max => false
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MinValue)
					return emulateBranch(2, false);	// min > x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MaxValue)
					return emulateBranch(2, false);	// x > max => false
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Bgt_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, (uint)int1.value > (uint)int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, (ulong)long1.value > (ulong)long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MinValue)
					return emulateBranch(2, false);	// min > x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MaxValue)
					return emulateBranch(2, false);	// x > max => false
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MinValue)
					return emulateBranch(2, false);	// min > x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MaxValue)
					return emulateBranch(2, false);	// x > max => false
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Ble() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, int1.value <= int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, long1.value <= long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MinValue)
					return emulateBranch(2, true);	// min <= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MaxValue)
					return emulateBranch(2, true);	// x <= max => true
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MinValue)
					return emulateBranch(2, true);	// min <= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MaxValue)
					return emulateBranch(2, true);	// x <= max => true
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Ble_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, (uint)int1.value <= (uint)int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, (ulong)long1.value <= (ulong)long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MinValue)
					return emulateBranch(2, true);	// min <= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MaxValue)
					return emulateBranch(2, true);	// x <= max => true
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MinValue)
					return emulateBranch(2, true);	// min <= x => true
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MaxValue)
					return emulateBranch(2, true);	// x <= max => true
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Blt() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, int1.value < int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, long1.value < long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MaxValue)
					return emulateBranch(2, false);	// max < x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MinValue)
					return emulateBranch(2, false);	// x < min => false
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MaxValue)
					return emulateBranch(2, false);	// max < x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MinValue)
					return emulateBranch(2, false);	// x < min => false
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Blt_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				return emulateBranch(2, (uint)int1.value < (uint)int2.value);
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				return emulateBranch(2, (ulong)long1.value < (ulong)long2.value);
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MaxValue)
					return emulateBranch(2, false);	// max < x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MinValue)
					return emulateBranch(2, false);	// x < min => false
				else
					return false;
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MaxValue)
					return emulateBranch(2, false);	// max < x => false
				else
					return false;
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MinValue)
					return emulateBranch(2, false);	// x < min => false
				else
					return false;
			}
			else {
				return false;
			}
		}

		bool emulate_Brfalse() {
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32)
				return emulateBranch(1, ((Int32Value)val1).value == 0);
			else if (val1.valueType == ValueType.Int64)
				return emulateBranch(1, ((Int64Value)val1).value == 0);
			else if (val1.valueType == ValueType.Null)
				return emulateBranch(1, true);
			else
				return false;
		}

		bool emulate_Brtrue() {
			var val1 = instructionEmulator.pop();

			//TODO: Support floats

			if (val1.valueType == ValueType.Int32)
				return emulateBranch(1, ((Int32Value)val1).value != 0);
			else if (val1.valueType == ValueType.Int64)
				return emulateBranch(1, ((Int64Value)val1).value != 0);
			else if (val1.valueType == ValueType.Null)
				return emulateBranch(1, false);
			else
				return false;
		}
	}
}
