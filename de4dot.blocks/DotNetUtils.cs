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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks {
	public enum FrameworkType {
		Unknown,
		Desktop,
		Silverlight,		// and WindowsPhone, XNA Xbox360
		CompactFramework,
		XNA,
		Zune,
	}

	public class CallCounter {
		Dictionary<IMethod, int> calls = new Dictionary<IMethod, int>(MethodEqualityComparer.CompareDeclaringTypes);

		public void Add(IMethod calledMethod) {
			calls.TryGetValue(calledMethod, out int count);
			calls[calledMethod] = count + 1;
		}

		public IMethod Most() => Most(out int numCalls);

		public IMethod Most(out int numCalls) {
			IMethod method = null;
			int callCount = 0;
			foreach (var key in calls.Keys) {
				if (calls[key] > callCount) {
					callCount = calls[key];
					method = key;
				}
			}
			numCalls = callCount;
			return method;
		}
	}

	public static class DotNetUtils {
		public static TypeDef GetModuleType(ModuleDef module) => module.GlobalType;
		public static MethodDef GetModuleTypeCctor(ModuleDef module) => module.GlobalType.FindStaticConstructor();

		public static bool IsEmpty(MethodDef method) {
			if (method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				var code = instr.OpCode.Code;
				if (code != Code.Nop && code != Code.Ret)
					return false;
			}
			return true;
		}

		public static bool IsEmptyObfuscated(MethodDef method) {
			if (method.Body == null)
				return false;
			int index = 0;
			var instr = GetInstruction(method.Body.Instructions, ref index);
			if (instr == null || instr.OpCode.Code != Code.Ret)
				return false;

			return true;
		}

		public static FieldDef FindFieldType(TypeDef typeDef, string typeName, bool isStatic) {
			if (typeDef == null)
				return null;
			foreach (var field in typeDef.Fields) {
				if (field.IsStatic == isStatic && field.FieldSig.GetFieldType().GetFullName() == typeName)
					return field;
			}
			return null;
		}

		public static IEnumerable<MethodDef> FindMethods(IEnumerable<MethodDef> methods, string returnType, string[] argsTypes) =>
			FindMethods(methods, returnType, argsTypes, true);

		public static IEnumerable<MethodDef> FindMethods(IEnumerable<MethodDef> methods, string returnType, string[] argsTypes, bool isStatic) {
			foreach (var method in methods) {
				var sig = method.MethodSig;
				if (sig == null || !method.HasBody || !sig.IsDefault)
					continue;
				if (method.IsStatic != isStatic || sig.Params.Count != argsTypes.Length)
					continue;
				if (sig.GenParamCount > 0)
					continue;
				if (sig.RetType.GetFullName() != returnType)
					continue;
				for (int i = 0; i < argsTypes.Length; i++) {
					if (sig.Params[i].GetFullName() != argsTypes[i])
						goto next;
				}
				yield return method;
			next: ;
			}
		}

		public static bool IsDelegate(IType type) {
			if (type == null)
				return false;
			var fn = type.FullName;
			return fn == "System.Delegate" || fn == "System.MulticastDelegate";
		}

		public static bool DerivesFromDelegate(TypeDef type) => type != null && IsDelegate(type.BaseType);

		public static bool IsMethod(IMethod method, string returnType, string parameters) =>
			method != null && method.FullName == returnType + " " + method.DeclaringType.FullName + "::" + method.Name + parameters;

		public static string GetDllName(string dll) {
			if (dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				return dll.Substring(0, dll.Length - 4);
			return dll;
		}

		public static bool HasPinvokeMethod(TypeDef type, string methodName) =>
			GetPInvokeMethod(type, methodName) != null;

		public static MethodDef GetPInvokeMethod(TypeDef type, string methodName) {
			if (type == null)
				return null;
			UTF8String mname = methodName;
			foreach (var method in type.Methods) {
				if (method.ImplMap == null)
					continue;
				if (UTF8String.Equals(method.ImplMap.Name, mname))
					return method;
			}
			return null;
		}

		public static MethodDef GetPInvokeMethod(TypeDef type, string dll, string funcName) {
			foreach (var method in type.Methods) {
				if (IsPinvokeMethod(method, dll, funcName))
					return method;
			}
			return null;
		}

		public static bool IsPinvokeMethod(MethodDef method, string dll, string funcName) {
			if (method == null)
				return false;
			if (method.ImplMap == null)
				return false;
			return method.ImplMap.IsPinvokeMethod(dll, funcName);
		}

		public static MethodDef GetMethod(ModuleDefMD module, IMethod method) {
			if (method == null)
				return null;
			return GetMethod(module, method, method.DeclaringType);
		}

		public static MethodDef GetMethod2(ModuleDefMD module, IMethod method) {
			if (method == null)
				return null;
			if (method is MethodDef)
				return (MethodDef)method;
			var git = method.DeclaringType.TryGetGenericInstSig();
			var dt = git == null ? method.DeclaringType : git.GenericType.TypeDefOrRef;
			return GetMethod(module, method, dt);
		}

		static MethodDef GetMethod(ModuleDefMD module, IMethod method, ITypeDefOrRef declaringType) {
			if (method == null)
				return null;
			if (method is MethodDef)
				return (MethodDef)method;
			return GetMethod(GetType(module, declaringType), method);
		}

		public static MethodDef GetMethod(TypeDef type, string returnType, string parameters) {
			foreach (var method in type.Methods) {
				if (IsMethod(method, returnType, parameters))
					return method;
			}
			return null;
		}

		public static MethodDef GetMethod2(ModuleDef module, IMethod method) {
			if (method == null)
				return null;
			return GetMethod(module, method, method.DeclaringType.ScopeType);
		}

		public static TypeDef GetType(ModuleDef module, TypeSig type) {
			type = type.RemovePinnedAndModifiers();
			var tdr = type as TypeDefOrRefSig;
			if (tdr == null)
				return null;
			return GetType(module, tdr.TypeDefOrRef);
		}

		public static TypeDef GetType(ModuleDef module, ITypeDefOrRef type) {
			var td = type as TypeDef;
			if (td == null) {
				if (type is TypeRef tr) {
					var trAsm = tr.DefinitionAssembly;
					var modAsm = module.Assembly;
					if (trAsm != null && modAsm != null && trAsm.Name == modAsm.Name)
						td = tr.Resolve();
				}
			}
			return td != null && td.Module == module ? td : null;
		}

		static MethodDef GetMethod(ModuleDef module, IMethod method, ITypeDefOrRef declaringType) {
			if (method == null)
				return null;
			if (method is MethodDef)
				return (MethodDef)method;
			return GetMethod(GetType(module, declaringType), method);
		}

		public static MethodDef GetMethod(TypeDef type, IMethod methodRef) {
			if (type == null || methodRef == null)
				return null;
			if (methodRef is MethodDef)
				return (MethodDef)methodRef;
			return type.FindMethod(methodRef.Name, methodRef.MethodSig);
		}

		public static IEnumerable<MethodDef> GetNormalMethods(TypeDef type) {
			foreach (var method in type.Methods) {
				if (method.HasImplMap)
					continue;
				if (method.IsConstructor)
					continue;

				yield return method;
			}
		}

		public static FieldDef GetField(ModuleDef module, IField field) {
			if (field == null)
				return null;
			if (field is FieldDef)
				return (FieldDef)field;
			return GetField(GetType(module, field.DeclaringType), field);
		}

		public static FieldDef GetField(TypeDef type, IField fieldRef) {
			if (type == null || fieldRef == null)
				return null;
			if (fieldRef is FieldDef)
				return (FieldDef)fieldRef;
			return type.FindField(fieldRef.Name, fieldRef.FieldSig);
		}

		public static FieldDef GetField(TypeDef type, string typeFullName) {
			if (type == null)
				return null;
			foreach (var field in type.Fields) {
				if (field.FieldSig.GetFieldType().GetFullName() == typeFullName)
					return field;
			}
			return null;
		}

		public static IEnumerable<IMethod> GetMethodCalls(MethodDef method) {
			var list = new List<IMethod>();
			if (method.HasBody) {
				foreach (var instr in method.Body.Instructions) {
					if (instr.Operand is IMethod calledMethod)
						list.Add(calledMethod);
				}
			}
			return list;
		}

		public static bool HasString(MethodDef method, string s) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldstr && (string)instr.Operand == s)
					return true;
			}
			return false;
		}

		public static IList<string> GetCodeStrings(MethodDef method) {
			var strings = new List<string>();
			if (method != null && method.Body != null) {
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code == Code.Ldstr)
						strings.Add((string)instr.Operand);
				}
			}
			return strings;
		}

		public static Resource GetResource(ModuleDef module, string name) =>
			GetResource(module, new List<string> { name });

		public static Resource GetResource(ModuleDef module, IEnumerable<string> strings) {
			if (!module.HasResources)
				return null;

			var resources = module.Resources;
			foreach (var tmp in strings) {
				var resourceName = RemoveFromNullChar(tmp);
				if (resourceName == null)
					continue;
				UTF8String name = resourceName;
				foreach (var resource in resources) {
					if (UTF8String.Equals(resource.Name, name))
						return resource;
				}
			}

			return null;
		}

		static string RemoveFromNullChar(string s) {
			int index = s.IndexOf((char)0);
			if (index < 0)
				return s;
			return s.Substring(0, index);
		}

		// Copies most things but not everything
		public static MethodDef Clone(MethodDef method) {
			var newMethod = new MethodDefUser(method.Name, method.MethodSig, method.ImplAttributes, method.Attributes);
			newMethod.Rid = method.Rid;
			newMethod.DeclaringType2 = method.DeclaringType;
			foreach (var pd in method.ParamDefs)
				newMethod.ParamDefs.Add(new ParamDefUser(pd.Name, pd.Sequence, pd.Attributes));
			foreach (var gp in method.GenericParameters) {
				var newGp = new GenericParamUser(gp.Number, gp.Flags, gp.Name);
				foreach (var gpc in gp.GenericParamConstraints)
					newGp.GenericParamConstraints.Add(new GenericParamConstraintUser(gpc.Constraint));
				newMethod.GenericParameters.Add(newGp);
			}
			newMethod.Body = new CilBody();
			CopyBodyFromTo(method, newMethod);
			return newMethod;
		}

		public static void CopyBody(MethodDef method, out IList<Instruction> instructions, out IList<ExceptionHandler> exceptionHandlers) {
			if (method == null || !method.HasBody) {
				instructions = new List<Instruction>();
				exceptionHandlers = new List<ExceptionHandler>();
				return;
			}

			var oldInstrs = method.Body.Instructions;
			var oldExHandlers = method.Body.ExceptionHandlers;
			instructions = new List<Instruction>(oldInstrs.Count);
			exceptionHandlers = new List<ExceptionHandler>(oldExHandlers.Count);
			var oldToIndex = Utils.CreateObjectToIndexDictionary(oldInstrs);

			foreach (var oldInstr in oldInstrs)
				instructions.Add(oldInstr.Clone());

			foreach (var newInstr in instructions) {
				var operand = newInstr.Operand;
				if (operand is Instruction)
					newInstr.Operand = instructions[oldToIndex[(Instruction)operand]];
				else if (operand is IList<Instruction> oldArray) {
					var newArray = new Instruction[oldArray.Count];
					for (int i = 0; i < oldArray.Count; i++)
						newArray[i] = instructions[oldToIndex[oldArray[i]]];
					newInstr.Operand = newArray;
				}
			}

			foreach (var oldEx in oldExHandlers) {
				var newEx = new ExceptionHandler(oldEx.HandlerType) {
					TryStart = GetInstruction(instructions, oldToIndex, oldEx.TryStart),
					TryEnd = GetInstruction(instructions, oldToIndex, oldEx.TryEnd),
					FilterStart = GetInstruction(instructions, oldToIndex, oldEx.FilterStart),
					HandlerStart = GetInstruction(instructions, oldToIndex, oldEx.HandlerStart),
					HandlerEnd = GetInstruction(instructions, oldToIndex, oldEx.HandlerEnd),
					CatchType = oldEx.CatchType,
				};
				exceptionHandlers.Add(newEx);
			}
		}

		static Instruction GetInstruction(IList<Instruction> instructions, IDictionary<Instruction, int> instructionToIndex, Instruction instruction) {
			if (instruction == null)
				return null;
			return instructions[instructionToIndex[instruction]];
		}

		public static void RestoreBody(MethodDef method, IEnumerable<Instruction> instructions, IEnumerable<ExceptionHandler> exceptionHandlers) {
			if (method == null || method.Body == null)
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

		public static void CopyBodyFromTo(MethodDef fromMethod, MethodDef toMethod) {
			if (fromMethod == toMethod)
				return;

			CopyBody(fromMethod, out var instructions, out var exceptionHandlers);
			RestoreBody(toMethod, instructions, exceptionHandlers);
			CopyLocalsFromTo(fromMethod, toMethod);
			UpdateInstructionOperands(fromMethod, toMethod);
		}

		static void CopyLocalsFromTo(MethodDef fromMethod, MethodDef toMethod) {
			var fromBody = fromMethod.Body;
			var toBody = toMethod.Body;

			toBody.Variables.Clear();
			foreach (var local in fromBody.Variables)
				toBody.Variables.Add(new Local(local.Type));
		}

		static void UpdateInstructionOperands(MethodDef fromMethod, MethodDef toMethod) {
			var fromBody = fromMethod.Body;
			var toBody = toMethod.Body;

			toBody.InitLocals = fromBody.InitLocals;
			toBody.MaxStack = fromBody.MaxStack;

			var newOperands = new Dictionary<object, object>();
			var fromParams = fromMethod.Parameters;
			var toParams = toMethod.Parameters;
			for (int i = 0; i < fromParams.Count; i++)
				newOperands[fromParams[i]] = toParams[i];
			for (int i = 0; i < fromBody.Variables.Count; i++)
				newOperands[fromBody.Variables[i]] = toBody.Variables[i];

			foreach (var instr in toBody.Instructions) {
				if (instr.Operand == null)
					continue;
				if (newOperands.TryGetValue(instr.Operand, out object newOperand))
					instr.Operand = newOperand;
			}
		}

		public static string GetCustomArgAsString(CustomAttribute cattr, int arg) {
			if (cattr == null || arg >= cattr.ConstructorArguments.Count)
				return null;
			var carg = cattr.ConstructorArguments[arg];
			if (carg.Type.GetElementType() != ElementType.String)
				return null;
			return UTF8String.ToSystemStringOrEmpty((UTF8String)carg.Value);
		}

		public static IEnumerable<MethodDef> GetCalledMethods(ModuleDef module, MethodDef method) {
			if (method != null && method.HasBody) {
				foreach (var call in method.Body.Instructions) {
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var methodRef = call.Operand as IMethod;
					if (methodRef == null)
						continue;
					var type = GetType(module, methodRef.DeclaringType);
					var methodDef = GetMethod(type, methodRef);
					if (methodDef != null)
						yield return methodDef;
				}
			}
		}

		public static IList<Instruction> GetInstructions(IList<Instruction> instructions, int i, params OpCode[] opcodes) {
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

		public static bool HasReturnValue(IMethod method) {
			if (method == null || method.MethodSig == null || method.MethodSig.RetType == null)
				return false;
			return method.MethodSig.RetType.RemovePinnedAndModifiers().ElementType != ElementType.Void;
		}

		public static Parameter GetParameter(IList<Parameter> parameters, int index) {
			if (0 <= index && index < parameters.Count)
				return parameters[index];
			return null;
		}

		public static TypeSig GetArg(IList<TypeSig> args, int index) {
			if (0 <= index && index < args.Count)
				return args[index];
			return null;
		}

		public static List<TypeSig> GetArgs(IMethod method) {
			var sig = method.MethodSig;
			var args = new List<TypeSig>(sig.Params.Count + 1);
			if (sig.ImplicitThis)
				args.Add(method.DeclaringType.ToTypeSig());
			foreach (var arg in sig.Params)
				args.Add(arg);
			return args;
		}

		public static int GetArgsCount(IMethod method) {
			var sig = method.MethodSig;
			if (sig == null)
				return 0;
			int count = sig.Params.Count;
			if (sig.ImplicitThis)
				count++;
			return count;
		}

		public static IList<TypeSig> ReplaceGenericParameters(GenericInstSig typeOwner, MethodSpec methodOwner, IList<TypeSig> types) {
			if (typeOwner == null && methodOwner == null)
				return types;
			for (int i = 0; i < types.Count; i++)
				types[i] = GetGenericArgument(typeOwner, methodOwner, types[i]);
			return types;
		}

		public static TypeSig GetGenericArgument(GenericInstSig typeOwner, MethodSpec methodOwner, TypeSig type) {
			var typeArgs = typeOwner?.GenericArguments;
			var genMethodArgs = methodOwner == null || methodOwner.GenericInstMethodSig == null ?
						null : methodOwner.GenericInstMethodSig.GenericArguments;
			return GenericArgsSubstitutor.Create(type, typeArgs, genMethodArgs);
		}

		public static Instruction GetInstruction(IList<Instruction> instructions, ref int index) {
			for (int i = 0; i < 10; i++) {
				if (index < 0 || index >= instructions.Count)
					return null;
				var instr = instructions[index++];
				if (instr.OpCode.Code == Code.Nop)
					continue;
				if (instr.OpCode.OpCodeType == OpCodeType.Prefix)
					continue;
				if (instr == null || (instr.OpCode.Code != Code.Br && instr.OpCode.Code != Code.Br_S))
					return instr;
				instr = instr.Operand as Instruction;
				if (instr == null)
					return null;
				index = instructions.IndexOf(instr);
			}
			return null;
		}

		public static TypeDefOrRefSig FindOrCreateTypeRef(ModuleDef module, AssemblyRef asmRef, string ns, string name, bool isValueType) {
			var typeRef = module.UpdateRowId(new TypeRefUser(module, ns, name, asmRef));
			if (isValueType)
				return new ValueTypeSig(typeRef);
			else
				return new ClassSig(typeRef);
		}

		public static FrameworkType GetFrameworkType(ModuleDefMD module) {
			foreach (var modRef in module.GetAssemblyRefs()) {
				if (modRef.Name != "mscorlib")
					continue;
				if (PublicKeyBase.IsNullOrEmpty2(modRef.PublicKeyOrToken))
					continue;
				switch (BitConverter.ToString(modRef.PublicKeyOrToken.Data).Replace("-", "").ToLowerInvariant()) {
				case "b77a5c561934e089":
					return FrameworkType.Desktop;
				case "7cec85d7bea7798e":
					return FrameworkType.Silverlight;
				case "969db8053d3322ac":
					return FrameworkType.CompactFramework;
				case "1c9e259686f921e0":
					return FrameworkType.XNA;
				case "e92a8b81eba7ceb7":
					return FrameworkType.Zune;
				}
			}

			return FrameworkType.Unknown;
		}

		public static int GetMethodCalls(MethodDef method, string methodFullName) {
			if (method == null || method.Body == null)
				return 0;

			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName == methodFullName)
					count++;
			}

			return count;
		}

		public static bool CallsMethod(MethodDef method, string methodFullName) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName == methodFullName)
					return true;
			}

			return false;
		}

		public static bool CallsMethod(MethodDef method, string returnType, string parameters) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				if (IsMethod(instr.Operand as IMethod, returnType, parameters))
					return true;
			}

			return false;
		}

		public static IList<Instruction> GetArgPushes(IList<Instruction> instrs, int index) =>
			GetArgPushes(instrs, ref index);

		public static IList<Instruction> GetArgPushes(IList<Instruction> instrs, ref int index) {
			if (index < 0 || index >= instrs.Count)
				return null;
			var startInstr = instrs[index];
			startInstr.CalculateStackUsage(false, out int pushes, out int pops);

			index--;
			int numArgs = pops;
			var args = new List<Instruction>(numArgs);
			int stackSize = numArgs;
			while (index >= 0 && args.Count != numArgs) {
				var instr = instrs[index--];
				instr.CalculateStackUsage(false, out pushes, out pops);
				if (instr.OpCode.Code == Code.Dup) {
					args.Add(instr);
					stackSize--;
				}
				else {
					if (pushes == 1)
						args.Add(instr);
					else if (pushes > 1)
						throw new NotImplementedException();
					stackSize -= pushes;

					if (pops != 0) {
						index++;
						if (GetArgPushes(instrs, ref index) == null)
							return null;
					}
				}

				if (stackSize < 0)
					return null;
			}
			if (args.Count != numArgs)
				return null;
			args.Reverse();
			return args;
		}
	}
}
