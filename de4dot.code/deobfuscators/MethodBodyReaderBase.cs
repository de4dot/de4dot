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

namespace de4dot.code.deobfuscators {
	abstract class MethodBodyReaderBase {
		protected BinaryReader reader;
		public IList<VariableDefinition> Locals { get; set; }
		public IList<Instruction> Instructions { get; set; }
		public IList<ExceptionHandler> ExceptionHandlers { get; set; }
		protected IList<ParameterDefinition> parameters;
		int currentOffset;

		public MethodBodyReaderBase(BinaryReader reader) {
			this.reader = reader;
		}

		protected void setLocals(IList<TypeReference> types) {
			Locals = new List<VariableDefinition>(types.Count);
			foreach (var type in types)
				Locals.Add(new VariableDefinition(type));
		}

		protected void readInstructions(int numInstrs) {
			Instructions = new Instruction[numInstrs];
			currentOffset = 0;
			for (int i = 0; i < Instructions.Count; i++)
				Instructions[i] = readOneInstruction();
			fixBranches();
		}

		protected void readInstructionsNumBytes(uint codeSize) {
			var instrs = new List<Instruction>();
			long endOffs = reader.BaseStream.Position + codeSize;
			while (reader.BaseStream.Position < endOffs)
				instrs.Add(readOneInstruction());
			if (reader.BaseStream.Position != endOffs)
				throw new ApplicationException("Could not read all instructions");
			Instructions = instrs;
			fixBranches();
		}

		Instruction readOneInstruction() {
			var instr = readInstruction();
			if (instr.OpCode.Code == Code.Switch) {
				int[] targets = (int[])instr.Operand;
				currentOffset += instr.OpCode.Size + 4 + 4 * targets.Length;
			}
			else
				currentOffset += instr.GetSize();
			return instr;
		}

		void fixBranches() {
			foreach (var instr in Instructions) {
				switch (instr.OpCode.OperandType) {
				case OperandType.InlineBrTarget:
				case OperandType.ShortInlineBrTarget:
					instr.Operand = getInstruction((int)instr.Operand);
					break;

				case OperandType.InlineSwitch:
					var intTargets = (int[])instr.Operand;
					var targets = new Instruction[intTargets.Length];
					for (int i = 0; i < intTargets.Length; i++)
						targets[i] = getInstruction(intTargets[i]);
					instr.Operand = targets;
					break;
				}
			}
		}

		protected Instruction getInstructionOrNull(int offset) {
			foreach (var instr in Instructions) {
				if (instr.Offset == offset)
					return instr;
			}
			return null;
		}

		protected Instruction getInstruction(int offset) {
			var instr = getInstructionOrNull(offset);
			if (instr != null)
				return instr;
			throw new ApplicationException(string.Format("No instruction found at offset {0:X4}", offset));
		}

		Instruction readInstruction() {
			int offset = currentOffset;
			var opcode = readOpCode();
			var instr = new Instruction {
				OpCode = opcode,
				Offset = offset,
			};
			instr.Operand = readOperand(instr);
			return instr;
		}

		object readOperand(Instruction instr) {
			switch (instr.OpCode.OperandType) {
			case OperandType.InlineBrTarget:
				return readInlineBrTarget(instr);
			case OperandType.InlineField:
				return readInlineField(instr);
			case OperandType.InlineI:
				return readInlineI(instr);
			case OperandType.InlineI8:
				return readInlineI8(instr);
			case OperandType.InlineMethod:
				return readInlineMethod(instr);
			case OperandType.InlineNone:
				return readInlineNone(instr);
			case OperandType.InlinePhi:
				return readInlinePhi(instr);
			case OperandType.InlineR:
				return readInlineR(instr);
			case OperandType.InlineSig:
				return readInlineSig(instr);
			case OperandType.InlineString:
				return readInlineString(instr);
			case OperandType.InlineSwitch:
				return readInlineSwitch(instr);
			case OperandType.InlineTok:
				return readInlineTok(instr);
			case OperandType.InlineType:
				return readInlineType(instr);
			case OperandType.InlineVar:
				return readInlineVar(instr);
			case OperandType.InlineArg:
				return readInlineArg(instr);
			case OperandType.ShortInlineBrTarget:
				return readShortInlineBrTarget(instr);
			case OperandType.ShortInlineI:
				return readShortInlineI(instr);
			case OperandType.ShortInlineR:
				return readShortInlineR(instr);
			case OperandType.ShortInlineVar:
				return readShortInlineVar(instr);
			case OperandType.ShortInlineArg:
				return readShortInlineArg(instr);
			default:
				throw new ApplicationException(string.Format("Unknown operand type {0}", instr.OpCode.OperandType));
			}
		}

		protected virtual int readInlineBrTarget(Instruction instr) {
			return currentOffset + instr.GetSize() + reader.ReadInt32();
		}

		protected abstract FieldReference readInlineField(Instruction instr);

		protected virtual int readInlineI(Instruction instr) {
			return reader.ReadInt32();
		}

		protected virtual long readInlineI8(Instruction instr) {
			return reader.ReadInt64();
		}

		protected abstract MethodReference readInlineMethod(Instruction instr);

		protected virtual object readInlineNone(Instruction instr) {
			return null;
		}

		protected virtual object readInlinePhi(Instruction instr) {
			return null;
		}

		protected virtual double readInlineR(Instruction instr) {
			return reader.ReadDouble();
		}

		protected abstract CallSite readInlineSig(Instruction instr);

		protected abstract string readInlineString(Instruction instr);

		protected virtual int[] readInlineSwitch(Instruction instr) {
			var targets = new int[reader.ReadInt32()];
			int offset = currentOffset + instr.OpCode.Size + 4 + 4 * targets.Length;
			for (int i = 0; i < targets.Length; i++)
				targets[i] = offset + reader.ReadInt32();
			return targets;
		}

		protected abstract MemberReference readInlineTok(Instruction instr);

		protected abstract TypeReference readInlineType(Instruction instr);

		protected virtual VariableDefinition readInlineVar(Instruction instr) {
			return Locals[reader.ReadUInt16()];
		}

		protected virtual ParameterDefinition readInlineArg(Instruction instr) {
			return parameters[reader.ReadUInt16()];
		}

		protected virtual int readShortInlineBrTarget(Instruction instr) {
			return currentOffset + instr.GetSize() + reader.ReadSByte();
		}

		protected virtual object readShortInlineI(Instruction instr) {
			if (instr.OpCode.Code == Code.Ldc_I4_S)
				return reader.ReadSByte();
			return reader.ReadByte();
		}

		protected virtual float readShortInlineR(Instruction instr) {
			return reader.ReadSingle();
		}

		protected virtual VariableDefinition readShortInlineVar(Instruction instr) {
			return Locals[reader.ReadByte()];
		}

		protected virtual ParameterDefinition readShortInlineArg(Instruction instr) {
			return parameters[reader.ReadByte()];
		}

		OpCode readOpCode() {
			var op = reader.ReadByte();
			if (op != 0xFE)
				return OpCodes.OneByteOpCode[op];
			return OpCodes.TwoBytesOpCode[reader.ReadByte()];
		}

		protected void readExceptionHandlers(int numExceptionHandlers) {
			ExceptionHandlers = new ExceptionHandler[numExceptionHandlers];
			for (int i = 0; i < ExceptionHandlers.Count; i++)
				ExceptionHandlers[i] = readExceptionHandler();
		}

		protected abstract ExceptionHandler readExceptionHandler();

		public virtual void restoreMethod(MethodDefinition method) {
			var body = method.Body;

			body.Variables.Clear();
			if (Locals != null) {
				foreach (var local in Locals)
					body.Variables.Add(local);
			}

			body.Instructions.Clear();
			if (Instructions != null) {
				foreach (var instr in Instructions)
					body.Instructions.Add(instr);
			}

			body.ExceptionHandlers.Clear();
			if (ExceptionHandlers != null) {
				foreach (var eh in ExceptionHandlers)
					body.ExceptionHandlers.Add(eh);
			}
		}
	}
}
