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
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class MethodBodyReader : MethodBodyReaderBase {
		ModuleDefinition module;
		ushort maxStackSize;

		public MethodBodyReader(ModuleDefinition module, BinaryReader reader)
			: base(reader) {
			this.module = module;
		}

		public void read(MethodDefinition method) {
			this.parameters = getParameters(method);
			this.Locals = getLocals(method);

			maxStackSize = (ushort)reader.ReadInt32();
			readInstructionsNumBytes(reader.ReadUInt32());
			readExceptionHandlers();
		}

		void readExceptionHandlers() {
			int totalSize = reader.ReadInt32();
			if (totalSize == 0)
				return;
			reader.ReadInt32();
			readExceptionHandlers((totalSize - 4) / 24);
		}

		static IList<ParameterDefinition> getParameters(MethodReference method) {
			return DotNetUtils.getParameters(method);
		}

		static IList<VariableDefinition> getLocals(MethodDefinition method) {
			if (method.Body == null)
				return new List<VariableDefinition>();
			return new List<VariableDefinition>(method.Body.Variables);
		}

		protected override FieldReference readInlineField(Instruction instr) {
			return (FieldReference)module.LookupToken(reader.ReadInt32());
		}

		protected override MethodReference readInlineMethod(Instruction instr) {
			return (MethodReference)module.LookupToken(reader.ReadInt32());
		}

		protected override CallSite readInlineSig(Instruction instr) {
			return module.ReadCallSite(new MetadataToken(reader.ReadUInt32()));
		}

		protected override string readInlineString(Instruction instr) {
			return module.GetUserString(reader.ReadUInt32());
		}

		protected override MemberReference readInlineTok(Instruction instr) {
			return (MemberReference)module.LookupToken(reader.ReadInt32());
		}

		protected override TypeReference readInlineType(Instruction instr) {
			return (TypeReference)module.LookupToken(reader.ReadInt32());
		}

		protected override ExceptionHandler readExceptionHandler() {
			var eh = new ExceptionHandler((ExceptionHandlerType)reader.ReadInt32());

			int tryOffset = reader.ReadInt32();
			eh.TryStart = getInstruction(tryOffset);
			eh.TryEnd = getInstructionOrNull(tryOffset + reader.ReadInt32());

			int handlerOffset = reader.ReadInt32();
			eh.HandlerStart = getInstruction(handlerOffset);
			eh.HandlerEnd = getInstructionOrNull(handlerOffset + reader.ReadInt32());

			switch (eh.HandlerType) {
			case ExceptionHandlerType.Catch:
				eh.CatchType = (TypeReference)module.LookupToken(reader.ReadInt32());
				break;

			case ExceptionHandlerType.Filter:
				eh.FilterStart = getInstruction(reader.ReadInt32());
				break;

			case ExceptionHandlerType.Finally:
			case ExceptionHandlerType.Fault:
			default:
				reader.ReadInt32();
				break;
			}

			return eh;
		}

		public override void restoreMethod(MethodDefinition method) {
			base.restoreMethod(method);
			method.Body.MaxStackSize = maxStackSize;
		}
	}
}
