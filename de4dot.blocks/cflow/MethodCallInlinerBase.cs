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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	public abstract class MethodCallInlinerBase : IBlocksDeobfuscator {
		// We can't catch all infinite loops, so inline methods at most this many times
		const int MAX_ITERATIONS = 10;

		protected Blocks blocks;
		protected Block block;
		int iteration;
		AccessChecker accessChecker;

		public bool ExecuteIfNotModified { get; set; }

		public void DeobfuscateBegin(Blocks blocks) {
			this.blocks = blocks;
			iteration = 0;
		}

		public bool Deobfuscate(List<Block> allBlocks) {
			if (iteration++ >= MAX_ITERATIONS)
				return false;

			bool modified = false;
			foreach (var block in allBlocks) {
				this.block = block;
				modified |= DeobfuscateInternal();
			}
			return modified;
		}

		protected abstract bool DeobfuscateInternal();

		protected class InstructionPatcher {
			readonly int patchIndex;
			public readonly int afterIndex;
			public readonly Instruction lastInstr;
			readonly Instr clonedInstr;
			public InstructionPatcher(int patchIndex, int afterIndex, Instruction lastInstr) {
				this.patchIndex = patchIndex;
				this.afterIndex = afterIndex;
				this.lastInstr = lastInstr;
				clonedInstr = new Instr(lastInstr.Clone());
			}

			public void Patch(Block block) => block.Instructions[patchIndex] = clonedInstr;
		}

		protected bool InlineLoadMethod(int patchIndex, MethodDef methodToInline, Instruction loadInstr, int instrIndex) {
			if (!IsReturn(methodToInline, instrIndex))
				return false;

			int methodArgsCount = DotNetUtils.GetArgsCount(methodToInline);
			for (int i = 0; i < methodArgsCount; i++)
				block.Insert(patchIndex++, OpCodes.Pop.ToInstruction());

			block.Instructions[patchIndex] = new Instr(loadInstr.Clone());
			return true;
		}

		protected bool InlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex) =>
			InlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);

		protected bool InlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex, int popLastArgs) =>
			PatchMethod(methodToInline, TryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, popLastArgs));

		protected bool PatchMethod(MethodDef methodToInline, InstructionPatcher patcher) {
			if (patcher == null)
				return false;

			if (!IsReturn(methodToInline, patcher.afterIndex))
				return false;

			patcher.Patch(block);
			return true;
		}

		protected InstructionPatcher TryInlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex) =>
			TryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);

		protected virtual Instruction OnAfterLoadArg(MethodDef methodToInline, Instruction instr, ref int instrIndex) => instr;

		protected InstructionPatcher TryInlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex, int popLastArgs) {
			int loadIndex = 0;
			int methodArgsCount = DotNetUtils.GetArgsCount(methodToInline);
			bool foundLdarga = false;
			while (instr != null && loadIndex < methodArgsCount) {
				bool isLdarg = true;
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					break;
				case Code.Ldarga:
				case Code.Ldarga_S:
					foundLdarga = true;
					break;
				default:
					isLdarg = false;
					break;
				}
				if (!isLdarg)
					break;

				if (instr.GetParameterIndex() != loadIndex)
					return null;
				loadIndex++;
				instr = DotNetUtils.GetInstruction(methodToInline.Body.Instructions, ref instrIndex);
				instr = OnAfterLoadArg(methodToInline, instr, ref instrIndex);
			}
			if (instr == null || loadIndex != methodArgsCount - popLastArgs)
				return null;

			switch (instr.OpCode.Code) {
			case Code.Call:
			case Code.Callvirt:
				if (foundLdarga)
					return null;
				var callInstr = instr;
				var calledMethod = callInstr.Operand as IMethod;
				if (calledMethod == null)
					return null;

				if (!IsCompatibleType(-1, calledMethod.MethodSig.RetType, methodToInline.MethodSig.RetType))
					return null;

				if (!CheckSameMethods(calledMethod, methodToInline, popLastArgs))
					return null;

				if (!HasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, callInstr);

			case Code.Newobj:
				if (foundLdarga)
					return null;
				var newobjInstr = instr;
				var ctor = newobjInstr.Operand as IMethod;
				if (ctor == null)
					return null;

				if (!IsCompatibleType(-1, ctor.DeclaringType, methodToInline.MethodSig.RetType))
					return null;

				var methodArgs = methodToInline.Parameters;
				var calledMethodArgs = DotNetUtils.GetArgs(ctor);
				if (methodArgs.Count + 1 - popLastArgs != calledMethodArgs.Count)
					return null;
				for (int i = 1; i < calledMethodArgs.Count; i++) {
					if (!IsCompatibleType(i, calledMethodArgs[i], methodArgs[i - 1].Type))
						return null;
				}

				if (!HasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, newobjInstr);

			case Code.Ldfld:
			case Code.Ldflda:
			case Code.Ldftn:
			case Code.Ldvirtftn:
			case Code.Ldlen:
			case Code.Initobj:
			case Code.Isinst:
			case Code.Castclass:
			case Code.Newarr:
			case Code.Ldtoken:
			case Code.Unbox_Any:
				var ldInstr = instr;
				if (methodArgsCount != 1)
					return null;

				if (instr.OpCode.OperandType != OperandType.InlineNone && !HasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, ldInstr);

			default:
				return null;
			}
		}

		bool HasAccessTo(object operand) {
			if (operand == null)
				return false;
			accessChecker.UserType = blocks.Method.DeclaringType;
			return accessChecker.CanAccess(operand) ?? GetDefaultAccessResult();
		}

		protected virtual bool GetDefaultAccessResult() => true;

		protected virtual bool IsReturn(MethodDef methodToInline, int instrIndex) {
			var instr = DotNetUtils.GetInstruction(methodToInline.Body.Instructions, ref instrIndex);
			return instr != null && instr.OpCode.Code == Code.Ret;
		}

		protected bool CheckSameMethods(IMethod method, MethodDef methodToInline) =>
			CheckSameMethods(method, methodToInline, 0);

		protected bool CheckSameMethods(IMethod method, MethodDef methodToInline, int ignoreLastMethodToInlineArgs) {
			var methodToInlineArgs = methodToInline.Parameters;
			var methodArgs = DotNetUtils.GetArgs(method);
			bool hasImplicitThis = method.MethodSig.ImplicitThis;
			if (methodToInlineArgs.Count - ignoreLastMethodToInlineArgs != methodArgs.Count)
				return false;
			for (int i = 0; i < methodArgs.Count; i++) {
				var methodArg = methodArgs[i];
				var methodToInlineArg = GetArgType(methodToInline, methodToInlineArgs[i].Type);
				if (!IsCompatibleType(i, methodArg, methodToInlineArg)) {
					if (i != 0 || !hasImplicitThis)
						return false;
					if (!IsCompatibleValueThisPtr(methodArg, methodToInlineArg))
						return false;
				}
			}

			return true;
		}

		static TypeSig GetArgType(MethodDef method, TypeSig arg) {
			if (arg.GetElementType() != ElementType.MVar)
				return arg;
			var mvar = (GenericMVar)arg;
			foreach (var gp in method.GenericParameters) {
				if (gp.Number != mvar.Number)
					continue;
				foreach (var gpc in gp.GenericParamConstraints)
					return gpc.Constraint.ToTypeSig();
			}
			return arg;
		}

		protected virtual bool IsCompatibleType(int paramIndex, IType origType, IType newType) =>
			new SigComparer().Equals(origType, newType);

		static bool IsCompatibleValueThisPtr(IType origType, IType newType) {
			var newByRef = newType as ByRefSig;
			if (newByRef == null)
				return false;
			if (!IsValueType(newByRef.Next) || !IsValueType(origType))
				return false;
			return new SigComparer().Equals(origType, newByRef.Next);
		}

		protected static bool IsValueType(IType type) {
			if (type == null)
				return false;
			var ts = type as TypeSig;
			if (ts == null)
				return type.IsValueType;
			return ts.IsValueType && ts.ElementType != ElementType.Void;
		}
	}
}
