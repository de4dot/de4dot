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
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class CsvmToCilMethodConverter : CsvmToCilMethodConverterBase {
		VmOpCodeHandlerDetector opCodeDetector;

		public CsvmToCilMethodConverter(IDeobfuscatorContext deobfuscatorContext, ModuleDefMD module, VmOpCodeHandlerDetector opCodeDetector)
			: base(deobfuscatorContext, module) => this.opCodeDetector = opCodeDetector;

		protected override List<Instruction> ReadInstructions(MethodDef cilMethod, CsvmMethodData csvmMethod) {
			var reader = new BinaryReader(new MemoryStream(csvmMethod.Instructions));
			var instrs = new List<Instruction>();
			var gpContext = GenericParamContext.Create(cilMethod);
			var handlerInfoReader = new OpCodeHandlerInfoReader(module, gpContext);

			int numVmInstrs = reader.ReadInt32();
			var vmInstrs = new ushort[numVmInstrs];
			for (int i = 0; i < numVmInstrs; i++)
				vmInstrs[i] = reader.ReadUInt16();

			uint offset = 0;
			for (int vmInstrIndex = 0; vmInstrIndex < numVmInstrs; vmInstrIndex++) {
				var composite = opCodeDetector.Handlers[vmInstrs[vmInstrIndex]];
				IList<HandlerTypeCode> handlerInfos = composite.HandlerTypeCodes;
				if (handlerInfos.Count == 0)
					handlerInfos = new HandlerTypeCode[] { HandlerTypeCode.Nop };
				for (int hi = 0; hi < handlerInfos.Count; hi++) {
					var instr = handlerInfoReader.Read(handlerInfos[hi], reader);
					instr.Offset = offset;
					offset += (uint)GetInstructionSize(instr);
					SetCilToVmIndex(instr, vmInstrIndex);
					if (hi == 0)
						SetVmIndexToCil(instr, vmInstrIndex);
					instrs.Add(instr);
				}
			}

			return instrs;
		}
	}
}
