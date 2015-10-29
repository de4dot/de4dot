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
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	class MethodReader : MethodBodyReaderBase {
		ModuleDefMD module;
		MethodFlags flags;
		TypeDef delegateType;
		bool hasDelegateTypeFlag;
		GenericParamContext gpContext;

		[Flags]
		enum MethodFlags {
			InitLocals = 1,
			HasLocals = 2,
			HasInstructions = 4,
			HasExceptionHandlers = 8,
			HasDelegateType = 0x10,
		}

		public TypeDef DelegateType {
			get { return delegateType; }
		}

		public bool InitLocals {
			get { return (flags & MethodFlags.InitLocals) != 0; }
		}

		bool HasLocals {
			get { return (flags & MethodFlags.HasLocals) != 0; }
		}

		bool HasInstructions {
			get { return (flags & MethodFlags.HasInstructions) != 0; }
		}

		bool HasExceptionHandlers {
			get { return (flags & MethodFlags.HasExceptionHandlers) != 0; }
		}

		bool HasDelegateType {
			get { return !hasDelegateTypeFlag || (flags & MethodFlags.HasDelegateType) != 0; }
		}

		public bool HasDelegateTypeFlag {
			get { return hasDelegateTypeFlag; }
			set { hasDelegateTypeFlag = value; }
		}

		public MethodReader(ModuleDefMD module, byte[] data, IList<Parameter> parameters)
			: base(MemoryImageStream.Create(data), parameters) {
			this.module = module;
		}

		public void Read(MethodDef method) {
			gpContext = GenericParamContext.Create(method);
			flags = (MethodFlags)reader.ReadByte();
			if (HasDelegateType) {
				delegateType = Resolve<TypeDef>(ReadTypeToken());
				if (!DotNetUtils.DerivesFromDelegate(delegateType))
					throw new ApplicationException("Invalid delegate type");
			}
			if (HasLocals)
				ReadLocals((int)reader.Read7BitEncodedUInt32());
			if (HasInstructions)
				ReadInstructions((int)reader.Read7BitEncodedUInt32());
			if (HasExceptionHandlers)
				ReadExceptionHandlers((int)reader.Read7BitEncodedUInt32());
		}

		int GetTypeDefOrRefToken(uint token) {
			switch (token & 3) {
			case 0: return 0x02000000 + (int)(token >> 2);
			case 1: return 0x01000000 + (int)(token >> 2);
			case 2: return 0x1B000000 + (int)(token >> 2);
			default: throw new ApplicationException("Invalid token");
			}
		}

		void ReadLocals(int numLocals) {
			var localsTypes = new List<TypeSig>();
			for (int i = 0; i < numLocals; i++)
				localsTypes.Add(ReadType());
			SetLocals(localsTypes);
		}

		T Resolve<T>(int token) {
			return (T)module.ResolveToken(token, gpContext);
		}

		int ReadTypeToken() {
			return GetTypeDefOrRefToken(reader.Read7BitEncodedUInt32());
		}

		TypeSig ReadType() {
			switch ((ElementType)reader.ReadByte()) {
			case ElementType.Void:		return module.CorLibTypes.Void;
			case ElementType.Boolean:	return module.CorLibTypes.Boolean;
			case ElementType.Char:		return module.CorLibTypes.Char;
			case ElementType.I1:		return module.CorLibTypes.SByte;
			case ElementType.U1:		return module.CorLibTypes.Byte;
			case ElementType.I2:		return module.CorLibTypes.Int16;
			case ElementType.U2:		return module.CorLibTypes.UInt16;
			case ElementType.I4:		return module.CorLibTypes.Int32;
			case ElementType.U4:		return module.CorLibTypes.UInt32;
			case ElementType.I8:		return module.CorLibTypes.Int64;
			case ElementType.U8:		return module.CorLibTypes.UInt64;
			case ElementType.R4:		return module.CorLibTypes.Single;
			case ElementType.R8:		return module.CorLibTypes.Double;
			case ElementType.String:	return module.CorLibTypes.String;
			case ElementType.Ptr:		return new PtrSig(ReadType());
			case ElementType.ByRef:		return new ByRefSig(ReadType());
			case ElementType.TypedByRef:return module.CorLibTypes.TypedReference;
			case ElementType.I:			return module.CorLibTypes.IntPtr;
			case ElementType.U:			return module.CorLibTypes.UIntPtr;
			case ElementType.Object:	return module.CorLibTypes.Object;
			case ElementType.SZArray:	return new SZArraySig(ReadType());
			case ElementType.Sentinel:	ReadType(); return new SentinelSig();
			case ElementType.Pinned:	return new PinnedSig(ReadType());

			case ElementType.ValueType:
			case ElementType.Class:
				return Resolve<ITypeDefOrRef>(ReadTypeToken()).ToTypeSig();

			case ElementType.Array:
				var arrayType = ReadType();
				uint rank = reader.Read7BitEncodedUInt32();
				return new ArraySig(arrayType, rank);

			case ElementType.GenericInst:
				reader.ReadByte();
				var genericType = Resolve<ITypeDefOrRef>(ReadTypeToken());
				int numGenericArgs = (int)reader.Read7BitEncodedUInt32();
				var git = new GenericInstSig(genericType.ToTypeSig() as ClassOrValueTypeSig);
				for (int i = 0; i < numGenericArgs; i++)
					git.GenericArguments.Add(ReadType());
				return git;

			case ElementType.Var:
			case ElementType.MVar:
			case ElementType.FnPtr:
			case ElementType.CModReqd:
			case ElementType.CModOpt:
			case ElementType.Internal:
			default:
				throw new ApplicationException("Invalid local element type");
			}
		}

		protected override IField ReadInlineField(Instruction instr) {
			return Resolve<IField>(reader.ReadInt32());
		}

		protected override IMethod ReadInlineMethod(Instruction instr) {
			return Resolve<IMethod>(reader.ReadInt32());
		}

		protected override MethodSig ReadInlineSig(Instruction instr) {
			var token = reader.ReadUInt32();
			if (MDToken.ToTable(token) != Table.StandAloneSig)
				return null;
			var sas = module.ResolveStandAloneSig(MDToken.ToRID(token), gpContext);
			return sas == null ? null : sas.MethodSig;
		}

		protected override string ReadInlineString(Instruction instr) {
			return module.ReadUserString(reader.ReadUInt32());
		}

		protected override ITokenOperand ReadInlineTok(Instruction instr) {
			return Resolve<ITokenOperand>(reader.ReadInt32());
		}

		protected override ITypeDefOrRef ReadInlineType(Instruction instr) {
			return Resolve<ITypeDefOrRef>(reader.ReadInt32());
		}

		void ReadExceptionHandlers(int numExceptionHandlers) {
			exceptionHandlers = new List<ExceptionHandler>(numExceptionHandlers);
			for (int i = 0; i < numExceptionHandlers; i++)
				Add(ReadExceptionHandler());
		}

		ExceptionHandler ReadExceptionHandler() {
			var eh = new ExceptionHandler((ExceptionHandlerType)(reader.Read7BitEncodedUInt32() & 7));

			uint tryOffset = reader.Read7BitEncodedUInt32();
			eh.TryStart = GetInstructionThrow(tryOffset);
			eh.TryEnd = GetInstruction(tryOffset + reader.Read7BitEncodedUInt32());

			uint handlerOffset = reader.Read7BitEncodedUInt32();
			eh.HandlerStart = GetInstructionThrow(handlerOffset);
			eh.HandlerEnd = GetInstruction(handlerOffset + reader.Read7BitEncodedUInt32());

			switch (eh.HandlerType) {
			case ExceptionHandlerType.Catch:
				eh.CatchType = Resolve<ITypeDefOrRef>(reader.ReadInt32());
				break;

			case ExceptionHandlerType.Filter:
				eh.FilterStart = GetInstructionThrow(reader.ReadUInt32());
				break;

			case ExceptionHandlerType.Finally:
			case ExceptionHandlerType.Fault:
			default:
				reader.ReadInt32();
				break;
			}

			return eh;
		}
	}
}
