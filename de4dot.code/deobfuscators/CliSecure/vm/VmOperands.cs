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

namespace de4dot.code.deobfuscators.CliSecure.vm {
	interface IVmOperand {
	}

	class TokenOperand : IVmOperand {
		public int token;	// any type of token
		public TokenOperand(int token) {
			this.token = token;
		}
	}

	class TargetDisplOperand : IVmOperand {
		public int displacement;	// number of instructions from current instr
		public TargetDisplOperand(int displacement) {
			this.displacement = displacement;
		}
	}

	class SwitchTargetDisplOperand : IVmOperand {
		public int[] targetDisplacements;	// number of instructions from current instr
		public SwitchTargetDisplOperand(int[] targetDisplacements) {
			this.targetDisplacements = targetDisplacements;
		}
	}

	class ArgOperand : IVmOperand {
		public ushort arg;
		public ArgOperand(ushort arg) {
			this.arg = arg;
		}
	}

	class LocalOperand : IVmOperand {
		public ushort local;
		public LocalOperand(ushort local) {
			this.local = local;
		}
	}

	// OpCode must be changed to ldfld / ldsfld
	class LoadFieldOperand : IVmOperand {
		public int token;
		public LoadFieldOperand(int token) {
			this.token = token;
		}
	}

	// OpCode must be changed to ldflda / ldsflda
	class LoadFieldAddressOperand : IVmOperand {
		public int token;
		public LoadFieldAddressOperand(int token) {
			this.token = token;
		}
	}

	// OpCode must be changed to stfld / stsfld
	class StoreFieldOperand : IVmOperand {
		public int token;
		public StoreFieldOperand(int token) {
			this.token = token;
		}
	}
}
