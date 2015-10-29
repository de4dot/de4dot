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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class CsvmInfo {
		ModuleDef module;

		public TypeDef VmHandlerBaseType;

		public MethodDef LogicalOpShrUn;
		public MethodDef LogicalOpShl;
		public MethodDef LogicalOpShr;
		public MethodDef LogicalOpAnd;
		public MethodDef LogicalOpXor;
		public MethodDef LogicalOpOr;

		public MethodDef CompareLt;
		public MethodDef CompareLte;
		public MethodDef CompareGt;
		public MethodDef CompareGte;
		public MethodDef CompareEq;
		public MethodDef CompareEqz;

		public MethodDef ArithmeticSubOvfUn;
		public MethodDef ArithmeticMulOvfUn;
		public MethodDef ArithmeticRemUn;
		public MethodDef ArithmeticRem;
		public MethodDef ArithmeticDivUn;
		public MethodDef ArithmeticDiv;
		public MethodDef ArithmeticMul;
		public MethodDef ArithmeticMulOvf;
		public MethodDef ArithmeticSub;
		public MethodDef ArithmeticSubOvf;
		public MethodDef ArithmeticAddOvfUn;
		public MethodDef ArithmeticAddOvf;
		public MethodDef ArithmeticAdd;

		public MethodDef UnaryNot;
		public MethodDef UnaryNeg;

		public MethodDef ArgsGet;
		public MethodDef ArgsSet;
		public MethodDef LocalsGet;
		public MethodDef LocalsSet;

		public CsvmInfo(ModuleDef module) {
			this.module = module;
		}

		public bool Initialize() {
			return FindVmHandlerBase() &&
					FindLocalOpsMethods() &&
					FindComparerMethods() &&
					FindArithmeticMethods() &&
					FindUnaryOpsMethods() &&
					FindArgsLocals();
		}

		public bool FindVmHandlerBase() {
			foreach (var type in module.Types) {
				if (!type.IsPublic || !type.IsAbstract)
					continue;
				if (type.HasProperties || type.HasEvents)
					continue;
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (CountVirtual(type) != 2)
					continue;

				VmHandlerBaseType = type;
				return true;
			}

			return false;
		}

		public bool FindLocalOpsMethods() {
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (type.Methods.Count != 6 && type.Methods.Count != 7)
					continue;
				LogicalOpShrUn = FindLogicalOpMethodShrUn(type);
				if (LogicalOpShrUn == null)
					continue;
				LogicalOpShl = FindLogicalOpMethodShl(type);
				LogicalOpShr = FindLogicalOpMethodShr(type);
				LogicalOpAnd = FindLogicalOpMethodAnd(type);
				LogicalOpXor = FindLogicalOpMethodXor(type);
				LogicalOpOr = FindLogicalOpMethodOr(type);
				if (LogicalOpShrUn != null && LogicalOpShl != null &&
					LogicalOpShr != null && LogicalOpAnd != null &&
					LogicalOpXor != null && LogicalOpOr != null)
					return true;
			}

			return false;
		}

		MethodDef FindLogicalOpMethodShrUn(TypeDef type) {
			return FindLogicalOpMethod(type, ElementType.U4, ElementType.I4, ElementType.U4, Code.Shr_Un);
		}

		MethodDef FindLogicalOpMethodShl(TypeDef type) {
			return FindLogicalOpMethod(type, ElementType.I4, ElementType.I4, ElementType.I4, Code.Shl);
		}

		MethodDef FindLogicalOpMethodShr(TypeDef type) {
			return FindLogicalOpMethod(type, ElementType.I4, ElementType.I4, ElementType.I4, Code.Shr);
		}

		MethodDef FindLogicalOpMethod(TypeDef type, ElementType e1, ElementType e2, ElementType e3, Code code) {
			foreach (var method in type.Methods) {
				if (!CheckLogicalMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 7; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[i + 1], e1))
						continue;
					var ldarg1 = instrs[i + 2];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckUnboxAny(instrs[i + 3], e2))
						continue;
					var ldci4 = instrs[i + 4];
					if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0x1F)
						continue;
					if (instrs[i + 5].OpCode.Code != Code.And)
						continue;
					if (instrs[i + 6].OpCode.Code != code)
						continue;
					if (!CheckBox(instrs[i + 7], e3))
						continue;

					return method;
				}
			}

			return null;
		}

		MethodDef FindLogicalOpMethodAnd(TypeDef type) {
			return FindLogicalOpMethod(type, Code.And);
		}

		MethodDef FindLogicalOpMethodXor(TypeDef type) {
			return FindLogicalOpMethod(type, Code.Xor);
		}

		MethodDef FindLogicalOpMethodOr(TypeDef type) {
			return FindLogicalOpMethod(type, Code.Or);
		}

		MethodDef FindLogicalOpMethod(TypeDef type, Code code) {
			foreach (var method in type.Methods) {
				if (!CheckLogicalMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[i + 1], ElementType.I4))
						continue;
					var ldarg1 = instrs[i + 2];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckUnboxAny(instrs[i + 3], ElementType.I4))
						continue;
					if (instrs[i + 4].OpCode.Code != code)
						continue;
					if (!CheckBox(instrs[i + 5], ElementType.I4))
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckLogicalMethodSig(MethodDef method) {
			return method != null &&
				method.IsStatic &&
				method.MethodSig.GetParamCount() == 2 &&
				method.MethodSig.RetType.GetElementType() == ElementType.Object &&
				method.MethodSig.Params[0].GetElementType() == ElementType.Object &&
				method.MethodSig.Params[1].GetElementType() == ElementType.Object;
		}

		public bool FindComparerMethods() {
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (type.Methods.Count != 9)
					continue;
				CompareLt = FindCompareLt(type);
				if (CompareLt == null)
					continue;
				CompareLte = FindCompareLte(type);
				CompareGt = FindCompareGt(type);
				CompareGte = FindCompareGte(type);
				CompareEq = FindCompareEq(type);
				CompareEqz = FindCompareEqz(type);
				if (CompareLt != null && CompareLte != null &&
					CompareGt != null && CompareGte != null &&
					CompareEq != null && CompareEqz != null)
					return true;
			}

			return false;
		}

		MethodDef FindCompareLt(TypeDef type) {
			return FindCompareMethod(type, Code.Clt, false);
		}

		MethodDef FindCompareLte(TypeDef type) {
			return FindCompareMethod(type, Code.Cgt, true);
		}

		MethodDef FindCompareGt(TypeDef type) {
			return FindCompareMethod(type, Code.Cgt, false);
		}

		MethodDef FindCompareGte(TypeDef type) {
			return FindCompareMethod(type, Code.Clt, true);
		}

		MethodDef FindCompareMethod(TypeDef type, Code code, bool invert) {
			foreach (var method in type.Methods) {
				if (!CheckCompareMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				int end = instrs.Count - 6;
				if (invert)
					end -= 2;
				for (int i = 0; i < end; i++) {
					int index = i;
					var ldarg0 = instrs[index++];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[index++], ElementType.I4))
						continue;
					var ldarg1 = instrs[index++];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckUnboxAny(instrs[index++], ElementType.I4))
						continue;
					if (instrs[index++].OpCode.Code != code)
						continue;
					if (invert) {
						var ldci4 = instrs[index++];
						if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0)
							continue;
						if (instrs[index++].OpCode.Code != Code.Ceq)
							continue;
					}
					if (!instrs[index++].IsStloc())
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckCompareMethodSig(MethodDef method) {
			if (method == null || !method.IsStatic)
				return false;
			var sig = method.MethodSig;
			if (sig == null || sig.GetParamCount() != 3)
				return false;
			if (sig.RetType.GetElementType() != ElementType.Boolean)
				return false;
			if (sig.Params[0].GetElementType() != ElementType.Object)
				return false;
			if (sig.Params[1].GetElementType() != ElementType.Object)
				return false;
			var arg2 = sig.Params[2] as ValueTypeSig;
			if (arg2 == null || arg2.TypeDef == null || !arg2.TypeDef.IsEnum)
				return false;

			return true;
		}

		MethodDef FindCompareEq(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!CheckCompareEqMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[i + 1], ElementType.I4))
						continue;
					var ldarg1 = instrs[i + 2];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckUnboxAny(instrs[i + 3], ElementType.I4))
						continue;
					if (instrs[i + 4].OpCode.Code != Code.Ceq)
						continue;
					if (!instrs[i + 5].IsStloc())
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckCompareEqMethodSig(MethodDef method) {
			return method != null &&
				method.IsStatic &&
				method.MethodSig.GetParamCount() == 2 &&
				method.MethodSig.RetType.GetElementType() == ElementType.Boolean &&
				method.MethodSig.Params[0].GetElementType() == ElementType.Object &&
				method.MethodSig.Params[1].GetElementType() == ElementType.Object;
		}

		MethodDef FindCompareEqz(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!CheckCompareEqzMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 4; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[i + 1], ElementType.I4))
						continue;
					var ldci4 = instrs[i + 2];
					if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0)
						continue;
					if (instrs[i + 3].OpCode.Code != Code.Ceq)
						continue;
					if (!instrs[i + 4].IsStloc())
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckCompareEqzMethodSig(MethodDef method) {
			return method != null &&
				method.IsStatic &&
				method.MethodSig.GetParamCount() == 1 &&
				method.MethodSig.RetType.GetElementType() == ElementType.Boolean &&
				method.MethodSig.Params[0].GetElementType() == ElementType.Object;
		}

		public bool FindArithmeticMethods() {
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (type.Methods.Count != 15)
					continue;
				ArithmeticSubOvfUn = FindArithmeticSubOvfUn(type);
				if (ArithmeticSubOvfUn == null)
					continue;
				ArithmeticMulOvfUn = FindArithmeticMulOvfUn(type);
				ArithmeticRemUn = FindArithmeticRemUn(type);
				ArithmeticRem = FindArithmeticRem(type);
				ArithmeticDivUn = FindArithmeticDivUn(type);
				ArithmeticDiv = FindArithmeticDiv(type);
				ArithmeticMul = FindArithmeticMul(type);
				ArithmeticMulOvf = FindArithmeticMulOvf(type);
				ArithmeticSub = FindArithmeticSub(type);
				ArithmeticSubOvf = FindArithmeticSubOvf(type);
				ArithmeticAddOvfUn = FindArithmeticAddOvfUn(type);
				ArithmeticAddOvf = FindArithmeticAddOvf(type);
				ArithmeticAdd = FindArithmeticAdd(type);

				if (ArithmeticSubOvfUn != null && ArithmeticMulOvfUn != null &&
					ArithmeticRemUn != null && ArithmeticRem != null &&
					ArithmeticDivUn != null && ArithmeticDiv != null &&
					ArithmeticMul != null && ArithmeticMulOvf != null &&
					ArithmeticSub != null && ArithmeticSubOvf != null &&
					ArithmeticAddOvfUn != null && ArithmeticAddOvf != null &&
					ArithmeticAdd != null)
					return true;
			}

			return false;
		}

		MethodDef FindArithmeticSubOvfUn(TypeDef type) {
			return FindArithmeticOpUn(type, Code.Sub_Ovf_Un);
		}

		MethodDef FindArithmeticMulOvfUn(TypeDef type) {
			return FindArithmeticOpUn(type, Code.Mul_Ovf_Un);
		}

		MethodDef FindArithmeticAddOvfUn(TypeDef type) {
			return FindArithmeticOpUn(type, Code.Add_Ovf_Un);
		}

		MethodDef FindArithmeticOpUn(TypeDef type, Code code) {
			foreach (var method in type.Methods) {
				if (!CheckArithmeticUnMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 8; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckCallvirt(instrs[i + 1], "System.Int32", "()"))
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Conv_Ovf_U4)
						continue;
					var ldarg1 = instrs[i + 3];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckCallvirt(instrs[i + 4], "System.Int32", "()"))
						continue;
					if (instrs[i + 5].OpCode.Code != Code.Conv_Ovf_U4)
						continue;
					if (instrs[i + 6].OpCode.Code != code)
						continue;
					if (!CheckBox(instrs[i + 7], ElementType.U4))
						continue;
					if (!instrs[i + 8].IsStloc())
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckArithmeticUnMethodSig(MethodDef method) {
			return method != null &&
				method.IsStatic &&
				method.MethodSig.GetParamCount() == 2 &&
				method.MethodSig.RetType.GetElementType() == ElementType.Object &&
				method.MethodSig.Params[0].GetElementType() == ElementType.Class &&
				method.MethodSig.Params[1].GetElementType() == ElementType.Class;
		}

		MethodDef FindArithmeticRemUn(TypeDef type) {
			return FindArithmeticDivOrRemUn(type, Code.Rem_Un);
		}

		MethodDef FindArithmeticDivUn(TypeDef type) {
			return FindArithmeticDivOrRemUn(type, Code.Div_Un);
		}

		MethodDef FindArithmeticDivOrRemUn(TypeDef type, Code code) {
			foreach (var method in type.Methods) {
				if (!CheckArithmeticUnMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 7; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckCallvirt(instrs[i + 1], "System.Int32", "()"))
						continue;
					var ldarg1 = instrs[i + 2];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckCallvirt(instrs[i + 3], "System.Int32", "()"))
						continue;
					if (instrs[i + 4].OpCode.Code != code)
						continue;
					if (!CheckBox(instrs[i + 5], ElementType.U4))
						continue;
					if (!instrs[i + 6].IsStloc())
						continue;

					return method;
				}
			}

			return null;
		}

		MethodDef FindArithmeticRem(TypeDef type) {
			return FindArithmeticOther(type, Code.Rem);
		}

		MethodDef FindArithmeticDiv(TypeDef type) {
			return FindArithmeticOther(type, Code.Div);
		}

		MethodDef FindArithmeticMul(TypeDef type) {
			return FindArithmeticOther(type, Code.Mul);
		}

		MethodDef FindArithmeticMulOvf(TypeDef type) {
			return FindArithmeticOther(type, Code.Mul_Ovf);
		}

		MethodDef FindArithmeticSub(TypeDef type) {
			return FindArithmeticOther(type, Code.Sub);
		}

		MethodDef FindArithmeticSubOvf(TypeDef type) {
			return FindArithmeticOther(type, Code.Sub_Ovf);
		}

		MethodDef FindArithmeticAdd(TypeDef type) {
			return FindArithmeticOther(type, Code.Add);
		}

		MethodDef FindArithmeticAddOvf(TypeDef type) {
			return FindArithmeticOther(type, Code.Add_Ovf);
		}

		MethodDef FindArithmeticOther(TypeDef type, Code code) {
			foreach (var method in type.Methods) {
				if (!CheckArithmeticOtherMethodSig(method))
					continue;
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 6; i++) {
					var ldarg0 = instrs[i];
					if (!ldarg0.IsLdarg() || ldarg0.GetParameterIndex() != 0)
						continue;
					if (!CheckUnboxAny(instrs[i + 1], ElementType.I4))
						continue;
					var ldarg1 = instrs[i + 2];
					if (!ldarg1.IsLdarg() || ldarg1.GetParameterIndex() != 1)
						continue;
					if (!CheckUnboxAny(instrs[i + 3], ElementType.I4))
						continue;
					if (instrs[i + 4].OpCode.Code != code)
						continue;
					if (!CheckBox(instrs[i + 5], ElementType.I4))
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckArithmeticOtherMethodSig(MethodDef method) {
			return method != null &&
				method.IsStatic &&
				method.MethodSig.GetParamCount() == 2 &&
				method.MethodSig.RetType.GetElementType() == ElementType.Object &&
				method.MethodSig.Params[0].GetElementType() == ElementType.Object &&
				method.MethodSig.Params[1].GetElementType() == ElementType.Object;
		}

		public bool FindUnaryOpsMethods() {
			UnaryNot = FindUnaryOpMethod1(Code.Not);
			UnaryNeg = FindUnaryOpMethod1(Code.Neg);
			if (UnaryNot != null && UnaryNeg != null)
				return true;

			return FindUnaryOpMethod2();
		}

		MethodDef FindUnaryOpMethod1(Code code) {
			foreach (var type in module.Types) {
				if (type.BaseType != VmHandlerBaseType)
					continue;
				if (type.Methods.Count != 4)
					continue;
				var method = FindUnaryMethod(type, code);
				if (method != null)
					return method;
			}
			return null;
		}

		bool FindUnaryOpMethod2() {
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (type.Methods.Count != 3)
					continue;

				UnaryNot = FindUnaryMethod(type, Code.Not);
				UnaryNeg = FindUnaryMethod(type, Code.Neg);
				if (UnaryNot != null && UnaryNeg != null)
					return true;
			}
			return false;
		}

		MethodDef FindUnaryMethod(TypeDef type, Code code) {
			foreach (var method in type.Methods) {
				if (!IsUnsaryMethod(method, code))
					continue;

				return method;
			}
			return null;
		}

		bool IsUnsaryMethod(MethodDef method, Code code) {
			if (!method.HasBody || !method.IsStatic)
				return false;
			if (!DotNetUtils.IsMethod(method, "System.Object", "(System.Object)"))
				return false;
			if (CountThrows(method) != 1)
				return false;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldarg = instrs[i];
				if (!ldarg.IsLdarg() || ldarg.GetParameterIndex() != 0)
					continue;
				if (!CheckUnboxAny(instrs[i + 1], ElementType.I4))
					continue;
				if (instrs[i + 2].OpCode.Code != code)
					continue;
				if (!CheckBox(instrs[i + 3], ElementType.I4))
					continue;
				if (!instrs[i + 4].IsStloc())
					continue;

				return true;
			}

			return false;
		}

		static int CountThrows(MethodDef method) {
			if (method == null || method.Body == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					count++;
			}
			return count;
		}

		public bool FindArgsLocals() {
			var vmState = FindVmState();
			if (vmState == null)
				return false;

			var ctor = vmState.FindMethod(".ctor");
			return FindArgsLocals(ctor, 1, out ArgsGet, out ArgsSet) &&
				FindArgsLocals(ctor, 2, out LocalsGet, out LocalsSet);
		}

		TypeDef FindVmState() {
			if (VmHandlerBaseType == null)
				return null;
			foreach (var method in VmHandlerBaseType.Methods) {
				if (method.IsStatic || !method.IsAbstract)
					continue;
				if (method.Parameters.Count != 2)
					continue;
				var arg1 = method.Parameters[1].Type.TryGetTypeDef();
				if (arg1 == null)
					continue;

				return arg1;
			}
			return null;
		}

		static bool FindArgsLocals(MethodDef ctor, int arg, out MethodDef getter, out MethodDef setter) {
			getter = null;
			setter = null;
			if (ctor == null || !ctor.HasBody)
				return false;

			setter = FindSetter(ctor, arg);
			if (setter == null)
				return false;

			var propField = GetPropField(setter);
			if (propField == null)
				return false;

			getter = FindGetter(ctor.DeclaringType, propField);
			return getter != null;
		}

		static MethodDef FindSetter(MethodDef ctor, int arg) {
			if (ctor == null || !ctor.HasBody)
				return null;

			var instrs = ctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldarg = instrs[i];
				if (!ldarg.IsLdarg() || ldarg.GetParameterIndex() != arg)
					continue;
				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var method = call.Operand as MethodDef;
				if (method == null)
					continue;
				if (method.DeclaringType != ctor.DeclaringType)
					continue;

				return method;
			}

			return null;
		}

		static FieldDef GetPropField(MethodDef method) {
			if (method == null || !method.HasBody)
				return null;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Stfld)
					continue;
				var field = instr.Operand as FieldDef;
				if (field == null || field.DeclaringType != method.DeclaringType)
					continue;

				return field;
			}

			return null;
		}

		static MethodDef FindGetter(TypeDef type, FieldDef propField) {
			foreach (var method in type.Methods) {
				if (method.IsStatic || !method.HasBody)
					continue;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Ldfld)
						continue;
					if (instr.Operand != propField)
						continue;

					return method;
				}
			}

			return null;
		}

		static bool CheckCallvirt(Instruction instr, string returnType, string parameters) {
			if (instr.OpCode.Code != Code.Callvirt)
				return false;
			return DotNetUtils.IsMethod(instr.Operand as IMethod, returnType, parameters);
		}

		bool CheckUnboxAny(Instruction instr, ElementType expectedType) {
			if (instr == null || instr.OpCode.Code != Code.Unbox_Any)
				return false;
			var typeSig = module.CorLibTypes.GetCorLibTypeSig(instr.Operand as ITypeDefOrRef);
			return typeSig.GetElementType() == expectedType;
		}

		bool CheckBox(Instruction instr, ElementType expectedType) {
			if (instr == null || instr.OpCode.Code != Code.Box)
				return false;
			var typeSig = module.CorLibTypes.GetCorLibTypeSig(instr.Operand as ITypeDefOrRef);
			return typeSig.GetElementType() == expectedType;
		}

		static int CountVirtual(TypeDef type) {
			int count = 0;
			foreach (var method in type.Methods) {
				if (method.IsVirtual)
					count++;
			}
			return count;
		}
	}
}
