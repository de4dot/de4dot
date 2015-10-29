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

using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v1 {
	class CsvmToCilMethodConverter : CsvmToCilMethodConverterBase {
		VmOpCodeHandlerDetector opCodeDetector;

		public CsvmToCilMethodConverter(IDeobfuscatorContext deobfuscatorContext, ModuleDefMD module, VmOpCodeHandlerDetector opCodeDetector)
			: base(deobfuscatorContext, module) {
			this.opCodeDetector = opCodeDetector;
		}

		protected override List<Instruction> ReadInstructions(MethodDef cilMethod, CsvmMethodData csvmMethod) {
			var gpContext = GenericParamContext.Create(cilMethod);
			var reader = new BinaryReader(new MemoryStream(csvmMethod.Instructions));
			var instrs = new List<Instruction>();
			uint offset = 0;
			while (reader.BaseStream.Position < reader.BaseStream.Length) {
				int vmOpCode = reader.ReadUInt16();
				var instr = opCodeDetector.Handlers[vmOpCode].Read(reader, module, gpContext);
				instr.Offset = offset;
				offset += (uint)GetInstructionSize(instr);
				SetCilToVmIndex(instr, instrs.Count);
				SetVmIndexToCil(instr, instrs.Count);
				instrs.Add(instr);
			}
			return instrs;
		}
	}
}
