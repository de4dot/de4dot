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
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	class MethodReader : MethodBodyReaderBase {
		ModuleDefinition module;
		MethodFlags flags;
		TypeDefinition delegateType;

		[Flags]
		enum MethodFlags {
			InitLocals = 1,
			HasLocals = 2,
			HasInstructions = 4,
			HasExceptionHandlers = 8,
		}

		public TypeDefinition DelegateType {
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

		public MethodReader(ModuleDefinition module, byte[] data, IList<ParameterDefinition> parameters)
			: base(new BinaryReader(new MemoryStream(data))) {
			this.module = module;
			this.parameters = parameters;
		}

		public void read() {
			flags = (MethodFlags)reader.ReadByte();
			delegateType = resolve<TypeDefinition>(readTypeToken());
			if (!DotNetUtils.derivesFromDelegate(delegateType))
				throw new ApplicationException("Invalid delegate type");
			if (HasLocals)
				readLocals(Utils.readEncodedInt32(reader));
			if (HasInstructions)
				readInstructions(Utils.readEncodedInt32(reader));
			if (HasExceptionHandlers)
				readExceptionHandlers(Utils.readEncodedInt32(reader));
		}

		int getTypeDefOrRefToken(uint token) {
			switch (token & 3) {
			case 0: return 0x02000000 + (int)(token >> 2);
			case 1: return 0x01000000 + (int)(token >> 2);
			case 2: return 0x1B000000 + (int)(token >> 2);
			default: throw new ApplicationException("Invalid token");
			}
		}

		void readLocals(int numLocals) {
			var localsTypes = new List<TypeReference>();
			for (int i = 0; i < numLocals; i++)
				localsTypes.Add(readType());
			setLocals(localsTypes);
		}

		T resolve<T>(int token) {
			return (T)module.LookupToken(token);
		}

		int readTypeToken() {
			return getTypeDefOrRefToken(Utils.readEncodedUInt32(reader));
		}

		TypeReference readType() {
			TypeReference elementType;
			switch ((ElementType)reader.ReadByte()) {
			case ElementType.Void: return module.TypeSystem.Void;
			case ElementType.Boolean: return module.TypeSystem.Boolean;
			case ElementType.Char: return module.TypeSystem.Char;
			case ElementType.I1: return module.TypeSystem.SByte;
			case ElementType.U1: return module.TypeSystem.Byte;
			case ElementType.I2: return module.TypeSystem.Int16;
			case ElementType.U2: return module.TypeSystem.UInt16;
			case ElementType.I4: return module.TypeSystem.Int32;
			case ElementType.U4: return module.TypeSystem.UInt32;
			case ElementType.I8: return module.TypeSystem.Int64;
			case ElementType.U8: return module.TypeSystem.UInt64;
			case ElementType.R4: return module.TypeSystem.Single;
			case ElementType.R8: return module.TypeSystem.Double;
			case ElementType.String: return module.TypeSystem.String;
			case ElementType.Ptr: return new PointerType(readType());
			case ElementType.ByRef: return new ByReferenceType(readType());
			case ElementType.TypedByRef: return module.TypeSystem.TypedReference;
			case ElementType.I: return module.TypeSystem.IntPtr;
			case ElementType.U: return module.TypeSystem.UIntPtr;
			case ElementType.Object: return module.TypeSystem.Object;
			case ElementType.SzArray: return new ArrayType(readType());
			case ElementType.Sentinel: return new SentinelType(readType());
			case ElementType.Pinned: return new PinnedType(readType());

			case ElementType.ValueType:
			case ElementType.Class:
				return resolve<TypeReference>(readTypeToken());

			case ElementType.Array:
				elementType = readType();
				int rank = Utils.readEncodedInt32(reader);
				return new ArrayType(elementType, rank);

			case ElementType.GenericInst:
				reader.ReadByte();
				elementType = resolve<TypeReference>(readTypeToken());
				int numGenericArgs = Utils.readEncodedInt32(reader);
				var git = new GenericInstanceType(elementType);
				for (int i = 0; i < numGenericArgs; i++)
					git.GenericArguments.Add(readType());
				return git;

			case ElementType.None:
			case ElementType.Var:
			case ElementType.MVar:
			case ElementType.FnPtr:
			case ElementType.CModReqD:
			case ElementType.CModOpt:
			case ElementType.Internal:
			case ElementType.Modifier:
			case ElementType.Type:
			case ElementType.Boxed:
			case ElementType.Enum:
			default:
				throw new ApplicationException("Invalid local element type");
			}
		}

		protected override FieldReference readInlineField(Instruction instr) {
			return resolve<FieldReference>(reader.ReadInt32());
		}

		protected override MethodReference readInlineMethod(Instruction instr) {
			return resolve<MethodReference>(reader.ReadInt32());
		}

		protected override CallSite readInlineSig(Instruction instr) {
			return module.ReadCallSite(new MetadataToken(reader.ReadUInt32()));
		}

		protected override string readInlineString(Instruction instr) {
			return module.GetUserString(reader.ReadUInt32());
		}

		protected override MemberReference readInlineTok(Instruction instr) {
			return resolve<MemberReference>(reader.ReadInt32());
		}

		protected override TypeReference readInlineType(Instruction instr) {
			return resolve<TypeReference>(reader.ReadInt32());
		}

		protected override ExceptionHandler readExceptionHandler() {
			var eh = new ExceptionHandler((ExceptionHandlerType)(Utils.readEncodedInt32(reader) & 7));

			int tryOffset = Utils.readEncodedInt32(reader);
			eh.TryStart = getInstruction(tryOffset);
			eh.TryEnd = getInstructionOrNull(tryOffset + Utils.readEncodedInt32(reader));

			int handlerOffset = Utils.readEncodedInt32(reader);
			eh.HandlerStart = getInstruction(handlerOffset);
			eh.HandlerEnd = getInstructionOrNull(handlerOffset + Utils.readEncodedInt32(reader));

			switch (eh.HandlerType) {
			case ExceptionHandlerType.Catch:
				eh.CatchType = resolve<TypeReference>(reader.ReadInt32());
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
	}
}
