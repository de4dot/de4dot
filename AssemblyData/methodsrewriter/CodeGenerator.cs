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
using System.Reflection;
using System.Reflection.Emit;
using dnlib.DotNet.Emit;
using dnlib.DotNet;

using OpCode = dnlib.DotNet.Emit.OpCode;
using OpCodes = dnlib.DotNet.Emit.OpCodes;
using OperandType = dnlib.DotNet.Emit.OperandType;
using ROpCode = System.Reflection.Emit.OpCode;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace AssemblyData.methodsrewriter {
	class CodeGenerator {
		static Dictionary<OpCode, ROpCode> dnlibToReflection = new Dictionary<OpCode, ROpCode>();
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
				if (!refDict.TryGetValue(opcode.Value, out var ropcode))
					continue;
				dnlibToReflection[opcode] = ropcode;
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

		public Type DelegateType => delegateType;

		public CodeGenerator(IMethodsRewriter methodsRewriter, string methodName) {
			this.methodsRewriter = methodsRewriter;
			this.methodName = methodName;
		}

		public void SetMethodInfo(MMethod methodInfo) {
			this.methodInfo = methodInfo;
			methodReturnType = ResolverUtils.GetReturnType(methodInfo.methodBase);
			methodParameters = GetMethodParameterTypes(methodInfo.methodBase);
			delegateType = Utils.GetDelegateType(methodReturnType, methodParameters);
		}

		public Delegate Generate(IList<Instruction> allInstructions, IList<ExceptionHandler> allExceptionHandlers) {
			this.allInstructions = allInstructions;
			this.allExceptionHandlers = allExceptionHandlers;

			var dm = new DynamicMethod(methodName, methodReturnType, methodParameters, methodInfo.methodBase.Module, true);
			var lastInstr = allInstructions[allInstructions.Count - 1];
			ilg = dm.GetILGenerator((int)lastInstr.Offset + lastInstr.GetSize());

			InitInstrToIndex();
			InitLocals();
			InitLabels();

			exceptionHandlersStack = new Stack<ExceptionHandler>();
			for (int i = 0; i < allInstructions.Count; i++) {
				UpdateExceptionHandlers(i);
				var instr = allInstructions[i];
				ilg.MarkLabel(labels[i]);
				if (instr.Operand is Operand)
					WriteSpecialInstr(instr, (Operand)instr.Operand);
				else
					WriteInstr(instr);
			}
			UpdateExceptionHandlers(-1);

			return dm.CreateDelegate(delegateType);
		}

		Instruction GetExceptionInstruction(int instructionIndex) => instructionIndex < 0 ? null : allInstructions[instructionIndex];

		void UpdateExceptionHandlers(int instructionIndex) {
			var instr = GetExceptionInstruction(instructionIndex);
			UpdateExceptionHandlers(instr);
			if (AddTryStart(instr))
				UpdateExceptionHandlers(instr);
		}

		void UpdateExceptionHandlers(Instruction instr) {
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
						ilg.BeginCatchBlock(Resolver.GetRtType(ex.CatchType));
				}
				if (ex.HandlerEnd == instr) {
					exceptionHandlersStack.Pop();
					if (exceptionHandlersStack.Count == 0 || !IsSameTryBlock(ex, exceptionHandlersStack.Peek()))
						ilg.EndExceptionBlock();
				}
				else
					break;
			}
		}

		bool AddTryStart(Instruction instr) {
			var list = new List<ExceptionHandler>();
			foreach (var ex in allExceptionHandlers) {
				if (ex.TryStart == instr)
					list.Add(ex);
			}
			list.Reverse();

			foreach (var ex in list) {
				if (exceptionHandlersStack.Count == 0 || !IsSameTryBlock(ex, exceptionHandlersStack.Peek()))
					ilg.BeginExceptionBlock();
				exceptionHandlersStack.Push(ex);
			}

			return list.Count > 0;
		}

		static bool IsSameTryBlock(ExceptionHandler ex1, ExceptionHandler ex2) => ex1.TryStart == ex2.TryStart && ex1.TryEnd == ex2.TryEnd;

		void InitInstrToIndex() {
			instrToIndex = new Dictionary<Instruction, int>(allInstructions.Count);
			for (int i = 0; i < allInstructions.Count; i++)
				instrToIndex[allInstructions[i]] = i;
		}

		void InitLocals() {
			locals = new List<LocalBuilder>();
			foreach (var local in methodInfo.methodDef.Body.Variables)
				locals.Add(ilg.DeclareLocal(Resolver.GetRtType(local.Type), local.Type.RemoveModifiers().IsPinned));
			tempObjLocal = ilg.DeclareLocal(typeof(object));
			tempObjArrayLocal = ilg.DeclareLocal(typeof(object[]));
		}

		void InitLabels() {
			labels = new List<Label>(allInstructions.Count);
			for (int i = 0; i < allInstructions.Count; i++)
				labels.Add(ilg.DefineLabel());
		}

		Type[] GetMethodParameterTypes(MethodBase method) {
			var list = new List<Type>();
			if (ResolverUtils.HasThis(method))
				list.Add(method.DeclaringType);

			foreach (var param in method.GetParameters())
				list.Add(param.ParameterType);

			thisArgIndex = list.Count;
			list.Add(methodsRewriter.GetType());

			return list.ToArray();
		}

		void WriteSpecialInstr(Instruction instr, Operand operand) {
			BindingFlags flags;
			switch (operand.type) {
			case Operand.Type.ThisArg:
				ilg.Emit(ConvertOpCode(instr.OpCode), (short)thisArgIndex);
				break;

			case Operand.Type.TempObj:
				ilg.Emit(ConvertOpCode(instr.OpCode), tempObjLocal);
				break;

			case Operand.Type.TempObjArray:
				ilg.Emit(ConvertOpCode(instr.OpCode), tempObjArrayLocal);
				break;

			case Operand.Type.OurMethod:
				flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
				var methodName = (string)operand.data;
				var ourMethod = methodsRewriter.GetType().GetMethod(methodName, flags);
				if (ourMethod == null)
					throw new ApplicationException($"Could not find method {methodName}");
				ilg.Emit(ConvertOpCode(instr.OpCode), ourMethod);
				break;

			case Operand.Type.NewMethod:
				var methodBase = (MethodBase)operand.data;
				var delegateType = methodsRewriter.GetDelegateType(methodBase);
				flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
				var invokeMethod = delegateType.GetMethod("Invoke", flags);
				ilg.Emit(ConvertOpCode(instr.OpCode), invokeMethod);
				break;

			case Operand.Type.ReflectionType:
				ilg.Emit(ConvertOpCode(instr.OpCode), (Type)operand.data);
				break;

			default:
				throw new ApplicationException($"Unknown operand type: {operand.type}");
			}
		}

		Label GetLabel(Instruction target) => labels[instrToIndex[target]];

		Label[] GetLabels(Instruction[] targets) {
			var labels = new Label[targets.Length];
			for (int i = 0; i < labels.Length; i++)
				labels[i] = GetLabel(targets[i]);
			return labels;
		}

		void WriteInstr(Instruction instr) {
			var opcode = ConvertOpCode(instr.OpCode);
			switch (instr.OpCode.OperandType) {
			case OperandType.InlineNone:
				ilg.Emit(opcode);
				break;

			case OperandType.InlineBrTarget:
			case OperandType.ShortInlineBrTarget:
				ilg.Emit(opcode, GetLabel((Instruction)instr.Operand));
				break;

			case OperandType.InlineSwitch:
				ilg.Emit(opcode, GetLabels((Instruction[])instr.Operand));
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
				var obj = Resolver.GetRtObject((ITokenOperand)instr.Operand);
				if (obj is ConstructorInfo)
					ilg.Emit(opcode, (ConstructorInfo)obj);
				else if (obj is MethodInfo)
					ilg.Emit(opcode, (MethodInfo)obj);
				else if (obj is FieldInfo)
					ilg.Emit(opcode, (FieldInfo)obj);
				else if (obj is Type)
					ilg.Emit(opcode, (Type)obj);
				else
					throw new ApplicationException($"Unknown type: {(obj == null ? obj : obj.GetType())}");
				break;

			case OperandType.InlineVar:
				ilg.Emit(opcode, checked((short)((IVariable)instr.Operand).Index));
				break;

			case OperandType.ShortInlineVar:
				ilg.Emit(opcode, checked((byte)((IVariable)instr.Operand).Index));
				break;

			case OperandType.InlineSig:	//TODO:
			default:
				throw new ApplicationException($"Unknown OperandType {instr.OpCode.OperandType}");
			}
		}

		ROpCode ConvertOpCode(OpCode opcode) {
			if (dnlibToReflection.TryGetValue(opcode, out var ropcode))
				return ropcode;
			return ROpCodes.Nop;
		}
	}
}
