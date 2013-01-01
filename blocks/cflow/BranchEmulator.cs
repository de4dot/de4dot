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

using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	public interface IBranchHandler {
		// stackArgs is the number of args used by the branch instruction (1 or 2)
		void handleNormal(int stackArgs, bool isTaken);

		// Returns true if the switch target was found (even if it was the fall-through)
		bool handleSwitch(Int32Value switchIndex);
	}

	public class BranchEmulator {
		IBranchHandler branchHandler;
		InstructionEmulator instructionEmulator;

		public BranchEmulator(InstructionEmulator instructionEmulator, IBranchHandler branchHandler) {
			this.instructionEmulator = instructionEmulator;
			this.branchHandler = branchHandler;
		}

		public bool emulate(Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Br:
			case Code.Br_S:		return emulate_Br();
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
			case Code.Switch:	return emulate_Switch();

			default:
				return false;
			}
		}

		bool emulateBranch(int stackArgs, Bool3 cond) {
			if (cond == Bool3.Unknown)
				return false;
			return emulateBranch(stackArgs, cond == Bool3.True);
		}

		bool emulateBranch(int stackArgs, bool isTaken) {
			branchHandler.handleNormal(stackArgs, isTaken);
			return true;
		}

		bool emulate_Br() {
			return emulateBranch(0, true);
		}

		bool emulate_Beq() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareEq((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareEq((Int64Value)val1, (Int64Value)val2));
			else if (val1.isNull() && val2.isNull())
				return emulateBranch(2, true);
			else
				return false;
		}

		bool emulate_Bne_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareNeq((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareNeq((Int64Value)val1, (Int64Value)val2));
			else if (val1.isNull() && val2.isNull())
				return emulateBranch(2, false);
			else
				return false;
		}

		bool emulate_Bge() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareGe((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareGe((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Bge_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareGe_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareGe_Un((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Bgt() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareGt((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareGt((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Bgt_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareGt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareGt_Un((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Ble() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareLe((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareLe((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Ble_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareLe_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareLe_Un((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Blt() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareLt((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareLt((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Blt_Un() {
			var val2 = instructionEmulator.pop();
			var val1 = instructionEmulator.pop();

			if (val1.isInt32() && val2.isInt32())
				return emulateBranch(2, Int32Value.compareLt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				return emulateBranch(2, Int64Value.compareLt_Un((Int64Value)val1, (Int64Value)val2));
			else
				return false;
		}

		bool emulate_Brfalse() {
			var val1 = instructionEmulator.pop();

			if (val1.isInt32())
				return emulateBranch(1, Int32Value.compareFalse((Int32Value)val1));
			else if (val1.isInt64())
				return emulateBranch(1, Int64Value.compareFalse((Int64Value)val1));
			else if (val1.isNull())
				return emulateBranch(1, true);
			else if (val1.isObject() || val1.isString())
				return emulateBranch(1, false);
			else
				return false;
		}

		bool emulate_Brtrue() {
			var val1 = instructionEmulator.pop();

			if (val1.isInt32())
				return emulateBranch(1, Int32Value.compareTrue((Int32Value)val1));
			else if (val1.isInt64())
				return emulateBranch(1, Int64Value.compareTrue((Int64Value)val1));
			else if (val1.isNull())
				return emulateBranch(1, false);
			else if (val1.isObject() || val1.isString())
				return emulateBranch(1, true);
			else
				return false;
		}

		bool emulate_Switch() {
			var val1 = instructionEmulator.pop();

			if (!val1.isInt32())
				return false;
			return branchHandler.handleSwitch((Int32Value)val1);
		}
	}
}
