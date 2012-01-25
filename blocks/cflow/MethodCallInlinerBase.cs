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

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	public abstract class MethodCallInlinerBase : IMethodCallInliner {
		// We can't catch all infinite loops, so inline methods at most this many times
		const int MAX_ITERATIONS = 10;

		protected Blocks blocks;
		protected Block block;
		int iteration;

		public void init(Blocks blocks, Block block) {
			this.blocks = blocks;
			this.block = block;
			this.iteration = 0;
		}

		public bool deobfuscate() {
			if (iteration++ >= MAX_ITERATIONS)
				return false;

			return deobfuscateInternal();
		}

		protected abstract bool deobfuscateInternal();

		protected bool inlineLoadMethod(int patchIndex, MethodDefinition methodToInline, Instruction loadInstr, int instrIndex) {
			var instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
			if (instr == null || instr.OpCode.Code != Code.Ret)
				return false;

			int methodArgsCount = DotNetUtils.getArgsCount(methodToInline);
			for (int i = 0; i < methodArgsCount; i++)
				block.insert(patchIndex++, Instruction.Create(OpCodes.Pop));

			block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(loadInstr));
			return true;
		}

		protected bool inlineOtherMethod(int patchIndex, MethodDefinition methodToInline, Instruction instr, int instrIndex, int popLastArgs = 0) {
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

				if (DotNetUtils.getArgIndex(instr) != loadIndex)
					return false;
				loadIndex++;
				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
			}
			if (instr == null || loadIndex != methodArgsCount - popLastArgs)
				return false;

			if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
				if (foundLdarga)
					return false;
				var callInstr = instr;
				var calledMethod = callInstr.Operand as MethodReference;
				if (calledMethod == null)
					return false;

				if (!isCompatibleType(calledMethod.MethodReturnType.ReturnType, methodToInline.MethodReturnType.ReturnType))
					return false;

				var methodArgs = DotNetUtils.getArgs(methodToInline);
				var calledMethodArgs = DotNetUtils.getArgs(calledMethod);
				if (methodArgs.Count - popLastArgs != calledMethodArgs.Count)
					return false;
				for (int i = 0; i < calledMethodArgs.Count; i++) {
					var calledMethodArg = calledMethodArgs[i];
					var methodArg = methodArgs[i];
					if (!isCompatibleType(calledMethodArg, methodArg)) {
						if (i != 0 || !calledMethod.HasImplicitThis)
							return false;
						if (!isCompatibleValueThisPtr(calledMethodArg, methodArg))
							return false;
					}
				}

				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
				if (instr == null || instr.OpCode.Code != Code.Ret)
					return false;

				block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(callInstr));
				return true;
			}
			else if (instr.OpCode.Code == Code.Newobj) {
				if (foundLdarga)
					return false;
				var newobjInstr = instr;
				var ctor = newobjInstr.Operand as MethodReference;
				if (ctor == null)
					return false;

				if (!isCompatibleType(ctor.DeclaringType, methodToInline.MethodReturnType.ReturnType))
					return false;

				var methodArgs = DotNetUtils.getArgs(methodToInline);
				var calledMethodArgs = DotNetUtils.getArgs(ctor);
				if (methodArgs.Count + 1 - popLastArgs != calledMethodArgs.Count)
					return false;
				for (int i = 1; i < calledMethodArgs.Count; i++) {
					if (!isCompatibleType(calledMethodArgs[i], methodArgs[i - 1]))
						return false;
				}

				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
				if (instr == null || instr.OpCode.Code != Code.Ret)
					return false;

				block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(newobjInstr));
				return true;
			}
			else if (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldflda ||
					instr.OpCode.Code == Code.Ldftn || instr.OpCode.Code == Code.Ldvirtftn) {
				var ldInstr = instr;
				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
				if (instr == null || instr.OpCode.Code != Code.Ret)
					return false;

				if (methodArgsCount != 1)
					return false;
				block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(ldInstr));
				return true;
			}

			return false;
		}

		protected virtual bool isCompatibleType(TypeReference origType, TypeReference newType) {
			return MemberReferenceHelper.compareTypes(origType, newType);
		}

		static bool isCompatibleValueThisPtr(TypeReference origType, TypeReference newType) {
			var newByRef = newType as ByReferenceType;
			if (newByRef == null)
				return false;
			if (!newByRef.ElementType.IsValueType || !origType.IsValueType)
				return false;
			return MemberReferenceHelper.compareTypes(origType, newByRef.ElementType);
		}
	}
}
