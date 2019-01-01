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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET {
	class MethodBodyReader : MethodBodyReaderBase {
		ImageReader imageReader;
		public int Flags2 { get; set; }
		public ushort MaxStack { get; set; }
		public MethodBodyReader(ImageReader imageReader) : base(imageReader.reader) => this.imageReader = imageReader;

		public void Read(IList<Parameter> parameters) {
			this.parameters = parameters;
			Flags2 = imageReader.reader.ReadInt16();
			MaxStack = imageReader.reader.ReadUInt16();
			SetLocals(imageReader.ReadTypeSigs());
			int len = imageReader.ReadVariableLengthInt32();
			reader.Position = imageReader.reader.Position;
			ReadInstructions(len);
			imageReader.reader.Position = reader.Position;
			ReadExceptionHandlers(imageReader.ReadVariableLengthInt32());
			reader.Position = imageReader.reader.Position;
		}

		protected override IField ReadInlineField(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			var res = imageReader.ReadFieldRef();
			reader.Position = imageReader.reader.Position;
			return res;
		}

		protected override IMethod ReadInlineMethod(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			var res = imageReader.ReadMethodRef();
			reader.Position = imageReader.reader.Position;
			return res;
		}

		protected override MethodSig ReadInlineSig(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			var res = imageReader.ReadCallSite();
			reader.Position = imageReader.reader.Position;
			return res;
		}

		protected override string ReadInlineString(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			var res = imageReader.ReadString();
			reader.Position = imageReader.reader.Position;
			return res;
		}

		protected override ITokenOperand ReadInlineTok(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			ITokenOperand res;
			switch (imageReader.reader.ReadByte()) {
			case 0: res = imageReader.ReadTypeSig().ToTypeDefOrRef(); break;
			case 1: res = imageReader.ReadFieldRef(); break;
			case 2: res = imageReader.ReadMethodRef(); break;
			default: throw new ApplicationException("Unknown token type");
			}
			reader.Position = imageReader.reader.Position;
			return res;
		}

		protected override ITypeDefOrRef ReadInlineType(Instruction instr) {
			imageReader.reader.Position = reader.Position;
			var res = imageReader.ReadTypeSig().ToTypeDefOrRef();
			reader.Position = imageReader.reader.Position;
			return res;
		}

		void ReadExceptionHandlers(int numExceptionHandlers) {
			exceptionHandlers = new List<ExceptionHandler>(numExceptionHandlers);
			for (int i = 0; i < numExceptionHandlers; i++)
				Add(ReadExceptionHandler());
		}

		ExceptionHandler ReadExceptionHandler() {
			var ehType = (ExceptionHandlerType)imageReader.reader.ReadByte();
			uint tryOffset = imageReader.ReadVariableLengthUInt32();
			uint tryLength = imageReader.ReadVariableLengthUInt32();
			uint handlerOffset = imageReader.ReadVariableLengthUInt32();
			uint handlerLength = imageReader.ReadVariableLengthUInt32();
			var catchType = imageReader.ReadTypeSig().ToTypeDefOrRef();
			uint filterOffset = imageReader.ReadVariableLengthUInt32();

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
