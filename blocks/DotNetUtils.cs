/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks {
	public class CallCounter {
		Dictionary<MethodReferenceAndDeclaringTypeKey, int> calls = new Dictionary<MethodReferenceAndDeclaringTypeKey, int>();

		public void add(MethodReference calledMethod) {
			int count;
			var key = new MethodReferenceAndDeclaringTypeKey(calledMethod);
			calls.TryGetValue(key, out count);
			calls[key] = count + 1;
		}

		public MethodReference most() {
			MethodReference method = null;
			int callCount = 0;
			foreach (var key in calls.Keys) {
				if (calls[key] > callCount) {
					callCount = calls[key];
					method = key.MethodReference;
				}
			}
			return method;
		}
	}

	public class MethodCalls {
		Dictionary<string, int> methodCalls = new Dictionary<string, int>(StringComparer.Ordinal);

		public void addMethodCalls(MethodDefinition method) {
			if (!method.HasBody)
				return;
			foreach (var instr in method.Body.Instructions) {
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod != null)
					add(calledMethod);
			}
		}

		public void add(MethodReference method) {
			string key = method.FullName;
			if (!methodCalls.ContainsKey(key))
				methodCalls[key] = 0;
			methodCalls[key]++;
		}

		public int count(string methodFullName) {
			int count;
			methodCalls.TryGetValue(methodFullName, out count);
			return count;
		}

		public bool called(string methodFullName) {
			return count(methodFullName) != 0;
		}
	}

	public static class DotNetUtils {
		public static bool isLdcI4(Instruction instruction) {
			return isLdcI4(instruction.OpCode.Code);
		}

		public static bool isLdcI4(Code code) {
			switch (code) {
			case Code.Ldc_I4_M1:
			case Code.Ldc_I4_0:
			case Code.Ldc_I4_1:
			case Code.Ldc_I4_2:
			case Code.Ldc_I4_3:
			case Code.Ldc_I4_4:
			case Code.Ldc_I4_5:
			case Code.Ldc_I4_6:
			case Code.Ldc_I4_7:
			case Code.Ldc_I4_8:
			case Code.Ldc_I4_S:
			case Code.Ldc_I4:
				return true;
			default:
				return false;
			}
		}

		public static int getLdcI4Value(Instruction instruction) {
			switch (instruction.OpCode.Code) {
			case Code.Ldc_I4_M1:return -1;
			case Code.Ldc_I4_0: return 0;
			case Code.Ldc_I4_1: return 1;
			case Code.Ldc_I4_2: return 2;
			case Code.Ldc_I4_3: return 3;
			case Code.Ldc_I4_4: return 4;
			case Code.Ldc_I4_5: return 5;
			case Code.Ldc_I4_6: return 6;
			case Code.Ldc_I4_7: return 7;
			case Code.Ldc_I4_8: return 8;
			case Code.Ldc_I4_S: return (sbyte)instruction.Operand;
			case Code.Ldc_I4:	return (int)instruction.Operand;
			default:
				throw new ApplicationException(string.Format("Not an ldc.i4 instruction: {0}", instruction));
			}
		}

		// Returns the variable or null if it's not a ldloc/stloc instruction. It does not return
		// a local variable if it's a ldloca/ldloca.s instruction.
		public static VariableDefinition getLocalVar(IList<VariableDefinition> locals, Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Stloc:
			case Code.Stloc_S:
				return (VariableDefinition)instr.Operand;

			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				return locals[instr.OpCode.Code - Code.Ldloc_0];

			case Code.Stloc_0:
			case Code.Stloc_1:
			case Code.Stloc_2:
			case Code.Stloc_3:
				return locals[instr.OpCode.Code - Code.Stloc_0];

			default:
				return null;
			}
		}

		public static bool isConditionalBranch(Code code) {
			switch (code) {
			case Code.Bge:
			case Code.Bge_S:
			case Code.Bge_Un:
			case Code.Bge_Un_S:
			case Code.Blt:
			case Code.Blt_S:
			case Code.Blt_Un:
			case Code.Blt_Un_S:
			case Code.Bgt:
			case Code.Bgt_S:
			case Code.Bgt_Un:
			case Code.Bgt_Un_S:
			case Code.Ble:
			case Code.Ble_S:
			case Code.Ble_Un:
			case Code.Ble_Un_S:
			case Code.Brfalse:
			case Code.Brfalse_S:
			case Code.Brtrue:
			case Code.Brtrue_S:
			case Code.Beq:
			case Code.Beq_S:
			case Code.Bne_Un:
			case Code.Bne_Un_S:
				return true;

			default:
				return false;
			}
		}

		public static TypeDefinition getModuleType(ModuleDefinition module) {
			foreach (var type in module.Types) {
				if (type.FullName == "<Module>")
					return type;
			}
			return null;
		}

		public static bool isEmpty(MethodDefinition method) {
			if (!method.HasBody)
				return false;
			foreach (var instr in method.Body.Instructions) {
				var code = instr.OpCode.Code;
				if (code != Code.Nop && code != Code.Ret)
					return false;
			}
			return true;
		}

		public static FieldDefinition findFieldType(TypeDefinition typeDefinition, string typeName, bool isStatic) {
			if (typeDefinition == null)
				return null;
			foreach (var field in typeDefinition.Fields) {
				if (field.FieldType.FullName == typeName && field.IsStatic == isStatic)
					return field;
			}
			return null;
		}

		public static IEnumerable<MethodDefinition> findMethods(IEnumerable<MethodDefinition> methods, string returnType, string[] argsTypes, bool isStatic = true) {
			foreach (var method in methods) {
				if (!method.HasBody || method.CallingConvention != MethodCallingConvention.Default)
					continue;
				if (method.IsStatic != isStatic || method.Parameters.Count != argsTypes.Length)
					continue;
				if (method.GenericParameters.Count > 0)
					continue;
				if (method.MethodReturnType.ReturnType.FullName != returnType)
					continue;
				for (int i = 0; i < argsTypes.Length; i++) {
					if (method.Parameters[i].ParameterType.FullName != argsTypes[i])
						goto next;
				}
				yield return method;
			next: ;
			}
		}

		public static bool isDelegateType(TypeDefinition type) {
			return type != null && type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate";
		}

		public static bool isSameAssembly(TypeReference type, string assembly) {
			return MemberReferenceHelper.getCanonicalizedScopeName(type.Scope) == assembly.ToLowerInvariant();
		}

		public static bool isMethod(MethodReference method, string returnType, string parameters) {
			return method != null && method.FullName == returnType + " " + method.DeclaringType.FullName + "::" + method.Name + parameters;
		}

		public static MethodDefinition getPInvokeMethod(TypeDefinition type, string dll, string funcName) {
			foreach (var method in type.Methods) {
				if (isPinvokeMethod(method, dll, funcName))
					return method;
			}
			return null;
		}

		public static bool isPinvokeMethod(MethodDefinition method, string dll, string funcName) {
			if (method == null)
				return false;
			if (!method.HasPInvokeInfo || method.PInvokeInfo.EntryPoint != funcName)
				return false;
			return getDllName(dll).Equals(getDllName(method.PInvokeInfo.Module.Name), StringComparison.OrdinalIgnoreCase);
		}

		public static string getDllName(string dll) {
			if (dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				return dll.Substring(0, dll.Length - 4);
			return dll;
		}

		public static MethodDefinition getMethod(TypeDefinition type, string name) {
			if (type == null)
				return null;
			foreach (var method in type.Methods) {
				if (method.Name == name)
					return method;
			}
			return null;
		}

		public static MethodDefinition getMethod(TypeDefinition type, MethodReference methodReference) {
			if (type == null || methodReference == null)
				return null;
			if (methodReference is MethodDefinition)
				return (MethodDefinition)methodReference;
			foreach (var method in type.Methods) {
				if (MemberReferenceHelper.compareMethodReference(method, methodReference))
					return method;
			}
			return null;
		}

		public static MethodDefinition getMethod(ModuleDefinition module, MethodReference method) {
			if (method == null)
				return null;
			if (method is MethodDefinition)
				return (MethodDefinition)method;
			return getMethod(getType(module, method.DeclaringType), method);
		}

		public static IEnumerable<MethodDefinition> getNormalMethods(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (method.HasPInvokeInfo)
					continue;
				if (method.Name == ".ctor" || method.Name == ".cctor")
					continue;

				yield return method;
			}
		}

		public static TypeDefinition getType(ModuleDefinition module, TypeReference typeReference) {
			if (typeReference == null)
				return null;
			if (typeReference is TypeDefinition)
				return (TypeDefinition)typeReference;
			foreach (var type in module.GetTypes()) {
				if (MemberReferenceHelper.compareTypes(type, typeReference))
					return type;
			}
			return null;
		}

		public static FieldDefinition getField(ModuleDefinition module, FieldReference field) {
			if (field is FieldDefinition)
				return (FieldDefinition)field;
			return getField(getType(module, field.DeclaringType), field);
		}

		public static FieldDefinition getField(TypeDefinition type, FieldReference fieldReference) {
			if (type == null || fieldReference == null)
				return null;
			if (fieldReference is FieldDefinition)
				return (FieldDefinition)fieldReference;
			foreach (var field in type.Fields) {
				if (MemberReferenceHelper.compareFieldReference(field, fieldReference))
					return field;
			}
			return null;
		}

		public static IEnumerable<MethodReference> getMethodCalls(MethodDefinition method) {
			var list = new List<MethodReference>();
			if (method.HasBody) {
				foreach (var instr in method.Body.Instructions) {
					var calledMethod = instr.Operand as MethodReference;
					if (calledMethod != null)
						list.Add(calledMethod);
				}
			}
			return list;
		}

		public static MethodCalls getMethodCallCounts(MethodDefinition method) {
			var methodCalls = new MethodCalls();
			methodCalls.addMethodCalls(method);
			return methodCalls;
		}

		public static IList<string> getCodeStrings(MethodDefinition method) {
			var strings = new List<string>();
			if (method != null && method.Body != null) {
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code == Code.Ldstr)
						strings.Add((string)instr.Operand);
				}
			}
			return strings;
		}

		public static Resource getResource(ModuleDefinition module, string name) {
			return getResource(module, new List<string> { name });
		}

		public static Resource getResource(ModuleDefinition module, IEnumerable<string> strings) {
			if (!module.HasResources)
				return null;

			var resources = module.Resources;
			foreach (var resourceName in strings) {
				if (resourceName == null)
					continue;
				foreach (var resource in resources) {
					if (resource.Name == resourceName)
						return resource;
				}
			}

			return null;
		}

		public static Instruction clone(Instruction instr) {
			return new Instruction {
				Offset = instr.Offset,
				OpCode = instr.OpCode,
				Operand = instr.Operand,
				SequencePoint = instr.SequencePoint,
			};
		}

		public static void copyBody(MethodDefinition method, out IList<Instruction> instructions, out IList<ExceptionHandler> exceptionHandlers) {
			if (method == null || !method.HasBody) {
				instructions = new List<Instruction>();
				exceptionHandlers = new List<ExceptionHandler>();
				return;
			}

			var oldInstrs = method.Body.Instructions;
			var oldExHandlers = method.Body.ExceptionHandlers;
			instructions = new List<Instruction>(oldInstrs.Count);
			exceptionHandlers = new List<ExceptionHandler>(oldExHandlers.Count);
			var oldToIndex = Utils.createObjectToIndexDictionary(oldInstrs);

			foreach (var oldInstr in oldInstrs)
				instructions.Add(clone(oldInstr));

			foreach (var newInstr in instructions) {
				var operand = newInstr.Operand;
				if (operand is Instruction)
					newInstr.Operand = instructions[oldToIndex[(Instruction)operand]];
				else if (operand is Instruction[]) {
					var oldArray = (Instruction[])operand;
					var newArray = new Instruction[oldArray.Length];
					for (int i = 0; i < oldArray.Length; i++)
						newArray[i] = instructions[oldToIndex[oldArray[i]]];
					newInstr.Operand = newArray;
				}
			}

			foreach (var oldEx in oldExHandlers) {
				var newEx = new ExceptionHandler(oldEx.HandlerType) {
					TryStart	= getInstruction(instructions, oldToIndex, oldEx.TryStart),
					TryEnd		= getInstruction(instructions, oldToIndex, oldEx.TryEnd),
					FilterStart	= getInstruction(instructions, oldToIndex, oldEx.FilterStart),
					HandlerStart= getInstruction(instructions, oldToIndex, oldEx.HandlerStart),
					HandlerEnd	= getInstruction(instructions, oldToIndex, oldEx.HandlerEnd),
					CatchType	= oldEx.CatchType,
				};
				exceptionHandlers.Add(newEx);
			}
		}

		static Instruction getInstruction(IList<Instruction> instructions, IDictionary<Instruction, int> instructionToIndex, Instruction instruction) {
			if (instruction == null)
				return null;
			return instructions[instructionToIndex[instruction]];
		}

		public static void restoreBody(MethodDefinition method, IEnumerable<Instruction> instructions, IEnumerable<ExceptionHandler> exceptionHandlers) {
			if (method == null || !method.HasBody)
				return;

			var bodyInstrs = method.Body.Instructions;
			bodyInstrs.Clear();
			foreach (var instr in instructions)
				bodyInstrs.Add(instr);

			var bodyExceptionHandlers = method.Body.ExceptionHandlers;
			bodyExceptionHandlers.Clear();
			foreach (var eh in exceptionHandlers)
				bodyExceptionHandlers.Add(eh);
		}

		public static IEnumerable<CustomAttribute> findAttributes(AssemblyDefinition asm, TypeReference attr) {
			var list = new List<CustomAttribute>();
			if (asm == null)
				return list;
			foreach (var cattr in asm.CustomAttributes) {
				if (MemberReferenceHelper.compareTypes(attr, cattr.AttributeType))
					list.Add(cattr);
			}
			return list;
		}

		public static string getCustomArgAsString(CustomAttribute cattr, int arg) {
			if (cattr == null || arg >= cattr.ConstructorArguments.Count)
				return null;
			var carg = cattr.ConstructorArguments[arg];
			if (carg.Type.FullName != "System.String")
				return null;
			return (string)carg.Value;
		}

		public static IEnumerable<Tuple<TypeDefinition, MethodDefinition>> getCalledMethods(ModuleDefinition module, MethodDefinition method) {
			if (method != null && method.HasBody) {
				foreach (var call in method.Body.Instructions) {
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var methodRef = call.Operand as MethodReference;
					var type = DotNetUtils.getType(module, methodRef.DeclaringType);
					var methodDef = DotNetUtils.getMethod(type, methodRef);
					if (methodDef != null) {
						yield return new Tuple<TypeDefinition, MethodDefinition> {
							Item1 = type,
							Item2 = methodDef,
						};
					}
				}
			}
		}

		public static IList<Instruction> getInstructions(IList<Instruction> instructions, int i, params OpCode[] opcodes) {
			if (i + opcodes.Length > instructions.Count)
				return null;
			if (opcodes.Length == 0)
				return new List<Instruction>();
			if (opcodes[0] != instructions[i].OpCode)
				return null;

			var list = new List<Instruction>(opcodes.Length);
			for (int j = 0; j < opcodes.Length; j++) {
				var instr = instructions[i + j];
				if (instr.OpCode != opcodes[j])
					return null;
				list.Add(instr);
			}
			return list;
		}

		public static bool hasReturnValue(IMethodSignature method) {
			return !MemberReferenceHelper.verifyType(method.MethodReturnType.ReturnType, "mscorlib", "System.Void");
		}

		public static void updateStack(Instruction instr, ref int stack, bool methodHasReturnValue) {
			int pushes, pops;
			calculateStackUsage(instr, methodHasReturnValue, out pushes, out pops);
			if (pops == -1)
				stack = 0;
			else
				stack += pushes - pops;
		}

		// Sets pops to -1 if the stack is supposed to be cleared
		public static void calculateStackUsage(Instruction instr, bool methodHasReturnValue, out int pushes, out int pops) {
			if (instr.OpCode.FlowControl == FlowControl.Call)
				calculateStackUsage_call(instr, out pushes, out pops);
			else
				calculateStackUsage_nonCall(instr, methodHasReturnValue, out pushes, out pops);
		}

		static void calculateStackUsage_call(Instruction instr, out int pushes, out int pops) {
			pushes = 0;
			pops = 0;

			var method = (IMethodSignature)instr.Operand;
			if (hasReturnValue(method) || (instr.OpCode.Code == Code.Newobj && method.HasThis))
				pushes++;

			if (method.HasParameters)
				pops += method.Parameters.Count;
			if (method.HasThis && instr.OpCode.Code != Code.Newobj)
				pops++;
		}

		// Sets pops to -1 if the stack is supposed to be cleared
		static void calculateStackUsage_nonCall(Instruction instr, bool methodHasReturnValue, out int pushes, out int pops) {
			StackBehaviour stackBehavior;

			pushes = 0;
			pops = 0;

			stackBehavior = instr.OpCode.StackBehaviourPush;
			switch (stackBehavior) {
			case StackBehaviour.Push0:
				break;

			case StackBehaviour.Push1:
			case StackBehaviour.Pushi:
			case StackBehaviour.Pushi8:
			case StackBehaviour.Pushr4:
			case StackBehaviour.Pushr8:
			case StackBehaviour.Pushref:
				pushes++;
				break;

			case StackBehaviour.Push1_push1:
				pushes += 2;
				break;

			case StackBehaviour.Varpush:	// only call, calli, callvirt which are handled elsewhere
			default:
				throw new ApplicationException(string.Format("Unknown push StackBehavior {0}", stackBehavior));
			}

			stackBehavior = instr.OpCode.StackBehaviourPop;
			switch (stackBehavior) {
			case StackBehaviour.Pop0:
				break;

			case StackBehaviour.Pop1:
			case StackBehaviour.Popi:
			case StackBehaviour.Popref:
				pops++;
				break;

			case StackBehaviour.Pop1_pop1:
			case StackBehaviour.Popi_pop1:
			case StackBehaviour.Popi_popi:
			case StackBehaviour.Popi_popi8:
			case StackBehaviour.Popi_popr4:
			case StackBehaviour.Popi_popr8:
			case StackBehaviour.Popref_pop1:
			case StackBehaviour.Popref_popi:
				pops += 2;
				break;

			case StackBehaviour.Popi_popi_popi:
			case StackBehaviour.Popref_popi_popi:
			case StackBehaviour.Popref_popi_popi8:
			case StackBehaviour.Popref_popi_popr4:
			case StackBehaviour.Popref_popi_popr8:
			case StackBehaviour.Popref_popi_popref:
				pops += 3;
				break;

			case StackBehaviour.PopAll:
				pops = -1;
				break;

			case StackBehaviour.Varpop:	// call, calli, callvirt, newobj (all handled elsewhere), and ret
				if (methodHasReturnValue)
					pops++;
				break;

			default:
				throw new ApplicationException(string.Format("Unknown pop StackBehavior {0}", stackBehavior));
			}
		}

		public static AssemblyNameReference getAssemblyNameReference(IMetadataScope scope) {
			if (scope is ModuleDefinition) {
				var moduleDefinition = (ModuleDefinition)scope;
				return moduleDefinition.Assembly.Name;
			}
			else if (scope is AssemblyNameReference)
				return (AssemblyNameReference)scope;

			throw new ApplicationException(string.Format("Unknown IMetadataScope type: {0}", scope.GetType()));
		}

		public static string getFullAssemblyName(IMetadataScope scope) {
			//TODO: Returning scope.Name is probably best since the method could fail.
			var asmRef = getAssemblyNameReference(scope);
			return asmRef.FullName;
		}

		public static bool isAssembly(IMetadataScope scope, string assemblySimpleName) {
			return scope.Name == assemblySimpleName ||
				scope.Name.StartsWith(assemblySimpleName + ",", StringComparison.Ordinal);
		}

		public static int getArgIndex(MethodReference method, Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Ldarg_0: return 0;
			case Code.Ldarg_1: return 1;
			case Code.Ldarg_2: return 2;
			case Code.Ldarg_3: return 3;

			case Code.Ldarga:
			case Code.Ldarga_S:
			case Code.Ldarg:
			case Code.Ldarg_S:
				return getArgIndex(method, instr.Operand as ParameterDefinition);
			}

			return -1;
		}

		public static int getArgIndex(MethodReference method, ParameterDefinition arg) {
			if (arg == null)
				return -1;
			if (method.HasThis)
				return arg.Index + 1;
			return arg.Index;
		}

		public static List<ParameterDefinition> getParameters(MethodReference method) {
			var args = new List<ParameterDefinition>(method.Parameters.Count + 1);
			if (method.HasThis)
				args.Add(new ParameterDefinition(method.DeclaringType));
			foreach (var arg in method.Parameters)
				args.Add(arg);
			return args;
		}

		public static ParameterDefinition getParameter(MethodReference method, Instruction instr) {
			return getParameter(getParameters(method), method, instr);
		}

		public static ParameterDefinition getParameter(IList<ParameterDefinition> parameters, MethodReference method, Instruction instr) {
			return getParameter(parameters, getArgIndex(method, instr));
		}

		public static ParameterDefinition getParameter(IList<ParameterDefinition> parameters, int index) {
			if (0 <= index && index < parameters.Count)
				return parameters[index];
			return null;
		}

		public static List<TypeReference> getArgs(MethodReference method) {
			var args = new List<TypeReference>(method.Parameters.Count + 1);
			if (method.HasThis)
				args.Add(method.DeclaringType);
			foreach (var arg in method.Parameters)
				args.Add(arg.ParameterType);
			return args;
		}

		public static TypeReference getArgType(MethodReference method, Instruction instr) {
			return getArgType(getArgs(method), method, instr);
		}

		public static TypeReference getArgType(IList<TypeReference> methodArgs, MethodReference method, Instruction instr) {
			return getArgType(methodArgs, getArgIndex(method, instr));
		}

		public static TypeReference getArgType(IList<TypeReference> methodArgs, int index) {
			if (0 <= index && index < methodArgs.Count)
				return methodArgs[index];
			return null;
		}

		public static int getArgsCount(MethodReference method) {
			int count = method.Parameters.Count;
			if (method.HasThis)
				count++;
			return count;
		}

		public static Instruction createLdci4(int value) {
			if (value == -1) return Instruction.Create(OpCodes.Ldc_I4_M1);
			if (value == 0) return Instruction.Create(OpCodes.Ldc_I4_0);
			if (value == 1) return Instruction.Create(OpCodes.Ldc_I4_1);
			if (value == 2) return Instruction.Create(OpCodes.Ldc_I4_2);
			if (value == 3) return Instruction.Create(OpCodes.Ldc_I4_3);
			if (value == 4) return Instruction.Create(OpCodes.Ldc_I4_4);
			if (value == 5) return Instruction.Create(OpCodes.Ldc_I4_5);
			if (value == 6) return Instruction.Create(OpCodes.Ldc_I4_6);
			if (value == 7) return Instruction.Create(OpCodes.Ldc_I4_7);
			if (value == 8) return Instruction.Create(OpCodes.Ldc_I4_8);
			if (sbyte.MinValue <= value && value <= sbyte.MaxValue)
				return Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)value);
			return Instruction.Create(OpCodes.Ldc_I4, value);
		}
	}
}
