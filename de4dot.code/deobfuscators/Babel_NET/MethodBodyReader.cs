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

using System;
using System.Collections.Generic;
using System.IO;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET {
	class MethodBodyReader : MethodBodyReaderBase {
		ImageReader imageReader;
		public int Flags2 { get; set; }
		public ushort MaxStack { get; set; }

		public MethodBodyReader(ImageReader imageReader, IBinaryReader reader)
			: base(reader) {
			this.imageReader = imageReader;
		}

		public void read(IList<Parameter> parameters) {
			this.parameters = parameters;
			Flags2 = reader.ReadInt16();
			MaxStack = reader.ReadUInt16();
			SetLocals(imageReader.readTypeSigs());
			ReadInstructions(imageReader.readVariableLengthInt32());
			readExceptionHandlers(imageReader.readVariableLengthInt32());
		}

		protected override IField ReadInlineField(Instruction instr) {
			return imageReader.readFieldRef();
		}

		protected override IMethod ReadInlineMethod(Instruction instr) {
			return imageReader.readMethodRef();
		}

		protected override MethodSig ReadInlineSig(Instruction instr) {
			return imageReader.readCallSite();
		}

		protected override string ReadInlineString(Instruction instr) {
			return imageReader.readString();
		}

		protected override ITokenOperand ReadInlineTok(Instruction instr) {
			switch (reader.ReadByte()) {
			case 0: return imageReader.readTypeSig().ToTypeDefOrRef();
			case 1: return imageReader.readFieldRef();
			case 2: return imageReader.readMethodRef();
			default: throw new ApplicationException("Unknown token type");
			}
		}

		protected override ITypeDefOrRef ReadInlineType(Instruction instr) {
			return imageReader.readTypeSig().ToTypeDefOrRef();
		}

		void readExceptionHandlers(int numExceptionHandlers) {
			exceptionHandlers = new List<ExceptionHandler>(numExceptionHandlers);
			for (int i = 0; i < numExceptionHandlers; i++)
				Add(readExceptionHandler());
		}

		ExceptionHandler readExceptionHandler() {
			var ehType = (ExceptionHandlerType)reader.ReadByte();
			uint tryOffset = imageReader.readVariableLengthUInt32();
			uint tryLength = imageReader.readVariableLengthUInt32();
			uint handlerOffset = imageReader.readVariableLengthUInt32();
			uint handlerLength = imageReader.readVariableLengthUInt32();
			var catchType = imageReader.readTypeSig().ToTypeDefOrRef();
			uint filterOffset = imageReader.readVariableLengthUInt32();

			var eh = new ExceptionHandler(ehType);
			eh.TryStart = GetInstructionThrow(tryOffset);
			eh.TryEnd = GetInstruction(tryOffset + tryLength);
			if (ehType == ExceptionHandlerType.Filter)
				eh.FilterStart = GetInstructionThrow(filterOffset);
			eh.HandlerStart = GetInstructionThrow(handlerOffset);
			eh.HandlerEnd = GetInstruction(handlerOffset + handlerLength);
			eh.CatchType = catchType;
			return eh;
		}
	}
}
