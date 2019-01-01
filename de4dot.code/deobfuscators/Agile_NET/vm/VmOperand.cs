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
	interface IVmOperand {
	}

	class TargetDisplOperand : IVmOperand {
		public readonly int Displacement;   // number of VM instructions from current VM instr
		public TargetDisplOperand(int displacement) => Displacement = displacement;
	}

	class SwitchTargetDisplOperand : IVmOperand {
		public readonly int[] TargetDisplacements;  // number of VM instructions from current VM instr
		public SwitchTargetDisplOperand(int[] targetDisplacements) => TargetDisplacements = targetDisplacements;
	}

	class ArgOperand : IVmOperand {
		public readonly ushort Arg;
		public ArgOperand(ushort arg) => Arg = arg;
	}

	class LocalOperand : IVmOperand {
		public readonly ushort Local;
		public LocalOperand(ushort local) => Local = local;
	}

	class FieldInstructionOperand : IVmOperand {
		public readonly OpCode StaticOpCode;
		public readonly OpCode InstanceOpCode;
		public readonly IField Field;

		public FieldInstructionOperand(OpCode staticOpCode, OpCode instanceOpCode, IField field) {
			StaticOpCode = staticOpCode;
			InstanceOpCode = instanceOpCode;
			Field = field;
		}
	}
}
