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

using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.code.deobfuscators.Babel_NET {
	class MethodBodyReader : MethodBodyReaderBase {
		ImageReader imageReader;
		public int Flags2 { get; set; }
		public short MaxStack { get; set; }

		public MethodBodyReader(ImageReader imageReader, BinaryReader reader)
			: base(reader) {
			this.imageReader = imageReader;
		}

		public void read(ParameterDefinition[] parameters) {
			this.parameters = parameters;
			Flags2 = reader.ReadInt16();
			MaxStack = reader.ReadInt16();
			setLocals(imageReader.readTypeReferences());
			readInstructions(imageReader.readVariableLengthInt32());
			readExceptionHandlers(imageReader.readVariableLengthInt32());
		}

		protected override FieldReference readInlineField(Instruction instr) {
			return imageReader.readFieldReference();
		}

		protected override MethodReference readInlineMethod(Instruction instr) {
			return imageReader.readMethodReference();
		}

		protected override CallSite readInlineSig(Instruction instr) {
			return imageReader.readCallSite();
		}

		protected override string readInlineString(Instruction instr) {
			return imageReader.readString();
		}

		protected override MemberReference readInlineTok(Instruction instr) {
			switch (reader.ReadByte()) {
			case 0: return imageReader.readTypeReference();
			case 1: return imageReader.readFieldReference();
			case 2: return imageReader.readMethodReference();
			default: throw new ApplicationException("Unknown token type");
			}
		}

		protected override TypeReference readInlineType(Instruction instr) {
			return imageReader.readTypeReference();
		}

		protected override ExceptionHandler readExceptionHandler() {
			var ehType = (ExceptionHandlerType)reader.ReadByte();
			int tryOffset = imageReader.readVariableLengthInt32();
			int tryLength = imageReader.readVariableLengthInt32();
			int handlerOffset = imageReader.readVariableLengthInt32();
			int handlerLength = imageReader.readVariableLengthInt32();
			var catchType = imageReader.readTypeReference();
			int filterOffset = imageReader.readVariableLengthInt32();

			var eh = new ExceptionHandler(ehType);
			eh.TryStart = getInstruction(tryOffset);
			eh.TryEnd = getInstructionOrNull(tryOffset + tryLength);
			if (ehType == ExceptionHandlerType.Filter)
				eh.FilterStart = getInstruction(filterOffset);
			eh.HandlerStart = getInstruction(handlerOffset);
			eh.HandlerEnd = getInstructionOrNull(handlerOffset + handlerLength);
			eh.CatchType = catchType;
			return eh;
		}
	}
}
