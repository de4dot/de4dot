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

		public bool ExecuteOnNoChange { get; set; }

		public void deobfuscateBegin(Blocks blocks) {
			this.blocks = blocks;
			iteration = 0;
		}

		public bool deobfuscate(List<Block> allBlocks) {
			if (iteration++ >= MAX_ITERATIONS)
				return false;

			bool changed = false;
			foreach (var block in allBlocks) {
				this.block = block;
				changed |= deobfuscateInternal();
			}
			return changed;
		}

		protected abstract bool deobfuscateInternal();

		protected class InstructionPatcher {
			readonly int patchIndex;
			public readonly int afterIndex;
			public readonly Instruction lastInstr;
			readonly Instr clonedInstr;
			public InstructionPatcher(int patchIndex, int afterIndex, Instruction lastInstr) {
				this.patchIndex = patchIndex;
				this.afterIndex = afterIndex;
				this.lastInstr = lastInstr;
				this.clonedInstr = new Instr(lastInstr.Clone());
			}

			public void patch(Block block) {
				block.Instructions[patchIndex] = clonedInstr;
			}
		}

		protected bool inlineLoadMethod(int patchIndex, MethodDef methodToInline, Instruction loadInstr, int instrIndex) {
			if (!isReturn(methodToInline, instrIndex))
				return false;

			int methodArgsCount = DotNetUtils.getArgsCount(methodToInline);
			for (int i = 0; i < methodArgsCount; i++)
				block.insert(patchIndex++, OpCodes.Pop.ToInstruction());

			block.Instructions[patchIndex] = new Instr(loadInstr.Clone());
			return true;
		}

		protected bool inlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex) {
			return inlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);
		}

		protected bool inlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex, int popLastArgs) {
			return patchMethod(methodToInline, tryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, popLastArgs));
		}

		protected bool patchMethod(MethodDef methodToInline, InstructionPatcher patcher) {
			if (patcher == null)
				return false;

			if (!isReturn(methodToInline, patcher.afterIndex))
				return false;

			patcher.patch(block);
			return true;
		}

		protected InstructionPatcher tryInlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex) {
			return tryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);
		}

		protected virtual Instruction onAfterLoadArg(MethodDef methodToInline, Instruction instr, ref int instrIndex) {
			return instr;
		}

		protected InstructionPatcher tryInlineOtherMethod(int patchIndex, MethodDef methodToInline, Instruction instr, int instrIndex, int popLastArgs) {
			int loadIndex = 0;
			int methodArgsCount = DotNetUtils.getArgsCount(methodToInline);
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
				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
				instr = onAfterLoadArg(methodToInline, instr, ref instrIndex);
			}
			if (instr == null || loadIndex != methodArgsCount - popLastArgs)
				return null;

			if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
				if (foundLdarga)
					return null;
				var callInstr = instr;
				var calledMethod = callInstr.Operand as IMethod;
				if (calledMethod == null)
					return null;

				if (!isCompatibleType(-1, calledMethod.MethodSig.RetType, methodToInline.MethodSig.RetType))
					return null;

				if (!checkSameMethods(calledMethod, methodToInline, popLastArgs))
					return null;

				if (!hasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, callInstr);
			}
			else if (instr.OpCode.Code == Code.Newobj) {
				if (foundLdarga)
					return null;
				var newobjInstr = instr;
				var ctor = newobjInstr.Operand as IMethod;
				if (ctor == null)
					return null;

				if (!isCompatibleType(-1, ctor.DeclaringType, methodToInline.MethodSig.RetType))
					return null;

				var methodArgs = methodToInline.Parameters;
				var calledMethodArgs = DotNetUtils.getArgs(ctor);
				if (methodArgs.Count + 1 - popLastArgs != calledMethodArgs.Count)
					return null;
				for (int i = 1; i < calledMethodArgs.Count; i++) {
					if (!isCompatibleType(i, calledMethodArgs[i], methodArgs[i - 1].Type))
						return null;
				}

				if (!hasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, newobjInstr);
			}
			else if (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldflda ||
					instr.OpCode.Code == Code.Ldftn || instr.OpCode.Code == Code.Ldvirtftn) {
				var ldInstr = instr;
				if (methodArgsCount != 1)
					return null;

				if (!hasAccessTo(instr.Operand))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, ldInstr);
			}

			return null;
		}

		bool hasAccessTo(object operand) {
			if (operand == null)
				return false;
			accessChecker.UserType = blocks.Method.DeclaringType;
			return accessChecker.CanAccess(operand) ?? getDefaultAccessResult();
		}

		protected virtual bool getDefaultAccessResult() {
			return true;
		}

		protected virtual bool isReturn(MethodDef methodToInline, int instrIndex) {
			var instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
			return instr != null && instr.OpCode.Code == Code.Ret;
		}

		protected bool checkSameMethods(IMethod method, MethodDef methodToInline) {
			return checkSameMethods(method, methodToInline, 0);
		}

		protected bool checkSameMethods(IMethod method, MethodDef methodToInline, int ignoreLastMethodToInlineArgs) {
			var methodToInlineArgs = methodToInline.Parameters;
			var methodArgs = DotNetUtils.getArgs(method);
			bool hasImplicitThis = method.MethodSig.ImplicitThis;
			if (methodToInlineArgs.Count - ignoreLastMethodToInlineArgs != methodArgs.Count)
				return false;
			for (int i = 0; i < methodArgs.Count; i++) {
				var methodArg = methodArgs[i];
				var methodToInlineArg = getArgType(methodToInline, methodToInlineArgs[i].Type);
				if (!isCompatibleType(i, methodArg, methodToInlineArg)) {
					if (i != 0 || !hasImplicitThis)
						return false;
					if (!isCompatibleValueThisPtr(methodArg, methodToInlineArg))
						return false;
				}
			}

			return true;
		}

		static TypeSig getArgType(MethodDef method, TypeSig arg) {
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

		protected virtual bool isCompatibleType(int paramIndex, IType origType, IType newType) {
			return new SigComparer().Equals(origType, newType);
		}

		static bool isCompatibleValueThisPtr(IType origType, IType newType) {
			var newByRef = newType as ByRefSig;
			if (newByRef == null)
				return false;
			if (!isValueType(newByRef.Next) || !isValueType(origType))
				return false;
			return new SigComparer().Equals(origType, newByRef.Next);
		}

		protected static bool isValueType(IType type) {
			if (type == null)
				return false;
			var ts = type as TypeSig;
			if (ts == null)
				return type.IsValueType;
			return ts.IsValueType && ts.ElementType != ElementType.Void;
		}
	}
}
