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

using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	public interface IBranchHandler {
		// stackArgs is the number of args used by the branch instruction (1 or 2)
		void HandleNormal(int stackArgs, bool isTaken);

		// Returns true if the switch target was found (even if it was the fall-through)
		bool HandleSwitch(Int32Value switchIndex);
	}

	public class BranchEmulator {
		IBranchHandler branchHandler;
		InstructionEmulator instructionEmulator;

		public BranchEmulator(InstructionEmulator instructionEmulator, IBranchHandler branchHandler) {
			this.instructionEmulator = instructionEmulator;
			this.branchHandler = branchHandler;
		}

		public bool Emulate(Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Br:
			case Code.Br_S:		return Emulate_Br();
			case Code.Beq:
			case Code.Beq_S:	return Emulate_Beq();
			case Code.Bge:
			case Code.Bge_S:	return Emulate_Bge();
			case Code.Bge_Un:
			case Code.Bge_Un_S:	return Emulate_Bge_Un();
			case Code.Bgt:
			case Code.Bgt_S:	return Emulate_Bgt();
			case Code.Bgt_Un:
			case Code.Bgt_Un_S:	return Emulate_Bgt_Un();
			case Code.Ble:
			case Code.Ble_S:	return Emulate_Ble();
			case Code.Ble_Un:
			case Code.Ble_Un_S:	return Emulate_Ble_Un();
			case Code.Blt:
			case Code.Blt_S:	return Emulate_Blt();
			case Code.Blt_Un:
			case Code.Blt_Un_S:	return Emulate_Blt_Un();
			case Code.Bne_Un:
			case Code.Bne_Un_S:	return Emulate_Bne_Un();
			case Code.Brfalse:
			case Code.Brfalse_S:return Emulate_Brfalse();
			case Code.Brtrue:
			case Code.Brtrue_S:	return Emulate_Brtrue();
			case Code.Switch:	return Emulate_Switch();

			default:
				return false;
			}
		}

		bool EmulateBranch(int stackArgs, Bool3 cond) {
			if (cond == Bool3.Unknown)
				return false;
			return EmulateBranch(stackArgs, cond == Bool3.True);
		}

		bool EmulateBranch(int stackArgs, bool isTaken) {
			branchHandler.HandleNormal(stackArgs, isTaken);
			return true;
		}

		bool Emulate_Br() {
			return EmulateBranch(0, true);
		}

		bool Emulate_Beq() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareEq((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareEq((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareEq((Real8Value)val1, (Real8Value)val2));
			else if (val1.IsNull() && val2.IsNull())
				return EmulateBranch(2, true);
			else
				return false;
		}

		bool Emulate_Bne_Un() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareNeq((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareNeq((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareNeq((Real8Value)val1, (Real8Value)val2));
			else if (val1.IsNull() && val2.IsNull())
				return EmulateBranch(2, false);
			else
				return false;
		}

		bool Emulate_Bge() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareGe((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareGe((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareGe((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Bge_Un() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareGe_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareGe_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareGe_Un((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Bgt() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareGt((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareGt((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareGt((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Bgt_Un() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareGt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareGt_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareGt_Un((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Ble() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareLe((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareLe((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareLe((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Ble_Un() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareLe_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareLe_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareLe_Un((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Blt() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareLt((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareLt((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareLt((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Blt_Un() {
			var val2 = instructionEmulator.Pop();
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				return EmulateBranch(2, Int32Value.CompareLt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				return EmulateBranch(2, Int64Value.CompareLt_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				return EmulateBranch(2, Real8Value.CompareLt_Un((Real8Value)val1, (Real8Value)val2));
			else
				return false;
		}

		bool Emulate_Brfalse() {
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32())
				return EmulateBranch(1, Int32Value.CompareFalse((Int32Value)val1));
			else if (val1.IsInt64())
				return EmulateBranch(1, Int64Value.CompareFalse((Int64Value)val1));
			else if (val1.IsReal8())
				return EmulateBranch(1, Real8Value.CompareFalse((Real8Value)val1));
			else if (val1.IsNull())
				return EmulateBranch(1, true);
			else if (val1.IsObject() || val1.IsString())
				return EmulateBranch(1, false);
			else
				return false;
		}

		bool Emulate_Brtrue() {
			var val1 = instructionEmulator.Pop();

			if (val1.IsInt32())
				return EmulateBranch(1, Int32Value.CompareTrue((Int32Value)val1));
			else if (val1.IsInt64())
				return EmulateBranch(1, Int64Value.CompareTrue((Int64Value)val1));
			else if (val1.IsReal8())
				return EmulateBranch(1, Real8Value.CompareTrue((Real8Value)val1));
			else if (val1.IsNull())
				return EmulateBranch(1, false);
			else if (val1.IsObject() || val1.IsString())
				return EmulateBranch(1, true);
			else
				return false;
		}

		bool Emulate_Switch() {
			var val1 = instructionEmulator.Pop();

			if (!val1.IsInt32())
				return false;
			return branchHandler.HandleSwitch((Int32Value)val1);
		}
	}
}
