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

using System.Collections.Generic;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Confuser {
	class Arg64ConstantsReader : ConstantsReader {
		long arg;
		bool firstTime;

		public long Arg {
			get { return arg; }
			set {
				arg = value;
				firstTime = true;
			}
		}

		public Arg64ConstantsReader(IList<Instruction> instrs, bool emulateConvInstrs)
			: base(instrs, emulateConvInstrs) {
		}

		protected override bool ProcessInstructionInt64(ref int index, Stack<ConstantInfo<long>> stack) {
			if (!firstTime)
				return false;
			firstTime = false;
			if (instructions[index].OpCode.Code != Code.Conv_I8)
				return false;

			stack.Push(new ConstantInfo<long>(index, arg));
			index = index + 1;
			return true;
		}
	}
}
