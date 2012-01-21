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
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

using OpCode = Mono.Cecil.Cil.OpCode;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using OperandType = Mono.Cecil.Cil.OperandType;
using ROpCode = System.Reflection.Emit.OpCode;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace AssemblyData.methodsrewriter {
	class CodeGenerator {
		static Dictionary<OpCode, ROpCode> cecilToReflection = new Dictionary<OpCode, ROpCode>();
		static CodeGenerator() {
			var refDict = new Dictionary<short, ROpCode>(0x100);
			foreach (var f in typeof(ROpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)) {
				if (f.FieldType != typeof(ROpCode))
					continue;
				var ropcode = (ROpCode)f.GetValue(null);
				refDict[ropcode.Value] = ropcode;
			}

			foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)) {
				if (f.FieldType != typeof(OpCode))
					continue;
				var opcode = (OpCode)f.GetValue(null);
				ROpCode ropcode;
				if (!refDict.TryGetValue(opcode.Value, out ropcode))
					continue;
				cecilToReflection[opcode] = ropcode;
			}
		}

		IMethodsRewriter methodsRewriter;
		string methodName;
		IList<Instruction> allInstructions;
		IList<ExceptionHandler> allExceptionHandlers;
		ILGenerator ilg;
		Type methodReturnType;
		Type[] methodParameters;
		Type delegateType;
		MMethod methodInfo;
		LocalBuilder tempObjLocal;
		LocalBuilder tempObjArrayLocal;
		int thisArgIndex;
		List<LocalBuilder> locals;
		List<Label> labels;
		Dictionary<Instruction, int> instrToIndex;
		Stack<ExceptionHandler> exceptionHandlersStack;

		public Type DelegateType {
			get { return delegateType; }
		}

		public CodeGenerator(IMethodsRewriter methodsRewriter, string methodName) {
			this.methodsRewriter = methodsRewriter;
			this.methodName = methodName;
		}

		public void setMethodInfo(MMethod methodInfo) {
			this.methodInfo = methodInfo;
			methodReturnType = ResolverUtils.getReturnType(methodInfo.methodBase);
			methodParameters = getMethodParameterTypes(methodInfo.methodBase);
			delegateType = Utils.getDelegateType(methodReturnType, methodParameters);
		}

		public Delegate generate(IList<Instruction> allInstructions, IList<ExceptionHandler> allExceptionHandlers) {
			this.allInstructions = allInstructions;
			this.allExceptionHandlers = allExceptionHandlers;

			var dm = new DynamicMethod(methodName, methodReturnType, methodParameters, methodInfo.methodBase.Module, true);
			var lastInstr = allInstructions[allInstructions.Count - 1];
			ilg = dm.GetILGenerator(lastInstr.Offset + lastInstr.GetSize());

			initInstrToIndex();
			initLocals();
			initLabels();

			exceptionHandlersStack = new Stack<ExceptionHandler>();
			for (int i = 0; i < allInstructions.Count; i++) {
				updateExceptionHandlers(i);
				var instr = allInstructions[i];
				ilg.MarkLabel(labels[i]);
				if (instr.Operand is Operand)
					writeSpecialInstr(instr, (Operand)instr.Operand);
				else
					writeInstr(instr);
			}
			updateExceptionHandlers(-1);

			return dm.CreateDelegate(delegateType);
		}

		Instruction getExceptionInstruction(int instructionIndex) {
			return instructionIndex < 0 ? null : allInstructions[instructionIndex];
		}

		void updateExceptionHandlers(int instructionIndex) {
			var instr = getExceptionInstruction(instructionIndex);
			updateExceptionHandlers(instr);
			if (addTryStart(instr))
				updateExceptionHandlers(instr);
		}

		void updateExceptionHandlers(Instruction instr) {
			while (exceptionHandlersStack.Count > 0) {
				var ex = exceptionHandlersStack.Peek();
				if (ex.TryEnd == instr) {
				}
				if (ex.FilterStart == instr) {
				}
				if (ex.HandlerStart == instr) {
					if (ex.HandlerType == ExceptionHandlerType.Finally)
						ilg.BeginFinallyBlock();
					else
						ilg.BeginCatchBlock(Resolver.getRtType(ex.CatchType));
				}
				if (ex.HandlerEnd == instr) {
					exceptionHandlersStack.Pop();
					if (exceptionHandlersStack.Count == 0 || !isSameTryBlock(ex, exceptionHandlersStack.Peek()))
						ilg.EndExceptionBlock();
				}
				else
					break;
			}
		}

		bool addTryStart(Instruction instr) {
			var list = new List<ExceptionHandler>();
			foreach (var ex in allExceptionHandlers) {
				if (ex.TryStart == instr)
					list.Add(ex);
			}
			list.Reverse();

			foreach (var ex in list) {
				if (exceptionHandlersStack.Count == 0 || !isSameTryBlock(ex, exceptionHandlersStack.Peek()))
					ilg.BeginExceptionBlock();
				exceptionHandlersStack.Push(ex);
			}

			return list.Count > 0;
		}

		static bool isSameTryBlock(ExceptionHandler ex1, ExceptionHandler ex2) {
			return ex1.TryStart == ex2.TryStart && ex1.TryEnd == ex2.TryEnd;
		}

		void initInstrToIndex() {
			instrToIndex = new Dictionary<Instruction, int>(allInstructions.Count);
			for (int i = 0; i < allInstructions.Count; i++)
				instrToIndex[allInstructions[i]] = i;
		}

		void initLocals() {
			locals = new List<LocalBuilder>();
			foreach (var local in methodInfo.methodDefinition.Body.Variables)
				locals.Add(ilg.DeclareLocal(Resolver.getRtType(local.VariableType), local.IsPinned));
			tempObjLocal = ilg.DeclareLocal(typeof(object));
			tempObjArrayLocal = ilg.DeclareLocal(typeof(object[]));
		}

		void initLabels() {
			labels = new List<Label>(allInstructions.Count);
			for (int i = 0; i < allInstructions.Count; i++)
				labels.Add(ilg.DefineLabel());
		}

		Type[] getMethodParameterTypes(MethodBase method) {
			var list = new List<Type>();
			if (ResolverUtils.hasThis(method))
				list.Add(method.DeclaringType);

			foreach (var param in method.GetParameters())
				list.Add(param.ParameterType);

			thisArgIndex = list.Count;
			list.Add(methodsRewriter.GetType());

			return list.ToArray();
		}

		void writeSpecialInstr(Instruction instr, Operand operand) {
			BindingFlags flags;
			switch (operand.type) {
			case Operand.Type.ThisArg:
				ilg.Emit(convertOpCode(instr.OpCode), (short)thisArgIndex);
				break;

			case Operand.Type.TempObj:
				ilg.Emit(convertOpCode(instr.OpCode), tempObjLocal);
				break;

			case Operand.Type.TempObjArray:
				ilg.Emit(convertOpCode(instr.OpCode), tempObjArrayLocal);
				break;

			case Operand.Type.OurMethod:
				flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
				var methodName = (string)operand.data;
				var ourMethod = methodsRewriter.GetType().GetMethod(methodName, flags);
				if (ourMethod == null)
					throw new ApplicationException(string.Format("Could not find method {0}", methodName));
				ilg.Emit(convertOpCode(instr.OpCode), ourMethod);
				break;

			case Operand.Type.NewMethod:
				var methodBase = (MethodBase)operand.data;
				var delegateType = methodsRewriter.getDelegateType(methodBase);
				flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
				var invokeMethod = delegateType.GetMethod("Invoke", flags);
				ilg.Emit(convertOpCode(instr.OpCode), invokeMethod);
				break;

			case Operand.Type.ReflectionType:
				ilg.Emit(convertOpCode(instr.OpCode), (Type)operand.data);
				break;

			default:
				throw new ApplicationException(string.Format("Unknown operand type: {0}", operand.type));
			}
		}

		Label getLabel(Instruction target) {
			return labels[instrToIndex[target]];
		}

		Label[] getLabels(Instruction[] targets) {
			var labels = new Label[targets.Length];
			for (int i = 0; i < labels.Length; i++)
				labels[i] = getLabel(targets[i]);
			return labels;
		}

		int getArgIndex(ParameterDefinition arg) {
			return arg.Sequence;
		}

		int getLocalIndex(VariableDefinition local) {
			return local.Index;
		}

		void writeInstr(Instruction instr) {
			var opcode = convertOpCode(instr.OpCode);
			switch (instr.OpCode.OperandType) {
			case OperandType.InlineNone:
				ilg.Emit(opcode);
				break;

			case OperandType.InlineBrTarget:
			case OperandType.ShortInlineBrTarget:
				ilg.Emit(opcode, getLabel((Instruction)instr.Operand));
				break;

			case OperandType.InlineSwitch:
				ilg.Emit(opcode, getLabels((Instruction[])instr.Operand));
				break;

			case OperandType.ShortInlineI:
				if (instr.OpCode.Code == Code.Ldc_I4_S)
					ilg.Emit(opcode, (sbyte)instr.Operand);
				else
					ilg.Emit(opcode, (byte)instr.Operand);
				break;

			case OperandType.InlineI:
				ilg.Emit(opcode, (int)instr.Operand);
				break;

			case OperandType.InlineI8:
				ilg.Emit(opcode, (long)instr.Operand);
				break;

			case OperandType.InlineR:
				ilg.Emit(opcode, (double)instr.Operand);
				break;

			case OperandType.ShortInlineR:
				ilg.Emit(opcode, (float)instr.Operand);
				break;

			case OperandType.InlineString:
				ilg.Emit(opcode, (string)instr.Operand);
				break;

			case OperandType.InlineTok:
			case OperandType.InlineType:
			case OperandType.InlineMethod:
			case OperandType.InlineField:
				var obj = Resolver.getRtObject((MemberReference)instr.Operand);
				if (obj is ConstructorInfo)
					ilg.Emit(opcode, (ConstructorInfo)obj);
				else if (obj is MethodInfo)
					ilg.Emit(opcode, (MethodInfo)obj);
				else if (obj is FieldInfo)
					ilg.Emit(opcode, (FieldInfo)obj);
				else if (obj is Type)
					ilg.Emit(opcode, (Type)obj);
				else
					throw new ApplicationException(string.Format("Unknown type: {0}", (obj == null ? obj : obj.GetType())));
				break;

			case OperandType.InlineArg:
				ilg.Emit(opcode, checked((short)getArgIndex((ParameterDefinition)instr.Operand)));
				break;

			case OperandType.ShortInlineArg:
				ilg.Emit(opcode, checked((byte)getArgIndex((ParameterDefinition)instr.Operand)));
				break;

			case OperandType.InlineVar:
				ilg.Emit(opcode, checked((short)getLocalIndex((VariableDefinition)instr.Operand)));
				break;

			case OperandType.ShortInlineVar:
				ilg.Emit(opcode, checked((byte)getLocalIndex((VariableDefinition)instr.Operand)));
				break;

			case OperandType.InlineSig:	//TODO:
			default:
				throw new ApplicationException(string.Format("Unknown OperandType {0}", instr.OpCode.OperandType));
			}
		}

		ROpCode convertOpCode(OpCode opcode) {
			ROpCode ropcode;
			if (cecilToReflection.TryGetValue(opcode, out ropcode))
				return ropcode;
			return ROpCodes.Nop;
		}
	}
}
