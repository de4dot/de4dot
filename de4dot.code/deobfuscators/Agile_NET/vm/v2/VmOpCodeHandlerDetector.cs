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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class MyDeobfuscator {
		CliSecureRtType cliSecureRtType;
		StringDecrypter stringDecrypter;
		StaticStringInliner staticStringInliner = new StaticStringInliner();

		public MyDeobfuscator(ModuleDefMD module) {
			cliSecureRtType = new CliSecureRtType(module);
			cliSecureRtType.Find(null);
			stringDecrypter = new StringDecrypter(module, cliSecureRtType.StringDecrypterInfos);
			stringDecrypter.Find();
			cliSecureRtType.FindStringDecrypterMethod();
			stringDecrypter.AddDecrypterInfos(cliSecureRtType.StringDecrypterInfos);
			stringDecrypter.Initialize();
			foreach (var info in stringDecrypter.StringDecrypterInfos)
				staticStringInliner.Add(info.Method, (method, gim, args) => stringDecrypter.Decrypt((string)args[0]));
		}

		void RestoreMethod(Blocks blocks) {
			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			blocks.GetCode(out allInstructions, out allExceptionHandlers);
			DotNetUtils.RestoreBody(blocks.Method, allInstructions, allExceptionHandlers);
		}

		public void DecryptStrings(MethodDef method) {
			var blocks = new Blocks(method);
			DecryptStrings(blocks);
			RestoreMethod(blocks);
		}

		public void DecryptStrings(Blocks blocks) {
			staticStringInliner.Decrypt(blocks);
		}

		public void Deobfuscate(MethodDef method) {
			DecryptStrings(method);
		}
	}

	class VmOpCodeHandlerDetector {
		const int NUM_HANDLERS = 78;
		ModuleDefMD module;
		List<VmOpCode> vmOpCodes;
		MyDeobfuscator deobfuscator;

		public IList<VmOpCode> Handlers {
			get { return vmOpCodes; }
		}

		public VmOpCodeHandlerDetector(ModuleDefMD module) {
			this.module = module;
		}

		public void FindHandlers() {
			if (vmOpCodes != null)
				return;

			deobfuscator = new MyDeobfuscator(module);
			var csvmInfo = new CsvmInfo(module);
			csvmInfo.Initialize();
			var vmHandlerTypes = FindVmHandlerTypes();
			if (vmHandlerTypes == null)
				throw new ApplicationException("Could not find CSVM opcode handler types");

			var composites = CreateCompositeOpCodeHandlers(csvmInfo, vmHandlerTypes);
			foreach (var handlerInfos in OpCodeHandlerInfos.HandlerInfos) {
				if (!DetectCompositeHandlers(composites, handlerInfos))
					continue;

				vmOpCodes = CreateVmOpCodes(composites);
				break;
			}
			if (vmOpCodes == null)
				throw new ApplicationException("Could not find any/all CSVM handlers");
		}

		static List<VmOpCode> CreateVmOpCodes(IList<CompositeOpCodeHandler> composites) {
			var list = new List<VmOpCode>(composites.Count);
			foreach (var composite in composites)
				list.Add(new VmOpCode(composite.TypeCodes));
			return list;
		}

		bool DetectCompositeHandlers(IEnumerable<CompositeOpCodeHandler> composites, IList<MethodSigInfo> handlerInfos) {
			var detector = new CompositeHandlerDetector(handlerInfos);
			foreach (var composite in composites) {
				if (!detector.FindHandlers(composite))
					return false;
			}
			return true;
		}

		static MethodDef SimplifyInstructions(MethodDef method) {
			if (method.Body == null)
				return method;
			method.Body.SimplifyMacros(method.Parameters);
			return method;
		}

		List<CompositeOpCodeHandler> CreateCompositeOpCodeHandlers(CsvmInfo csvmInfo, List<TypeDef> handlers) {
			var list = new List<CompositeOpCodeHandler>(handlers.Count);

			var sigCreator = CreateSigCreator(csvmInfo);
			foreach (var handler in handlers)
				list.Add(new CompositeOpCodeHandler(sigCreator.Create(GetExecMethod(handler))));

			return list;
		}

		MethodDef GetExecMethod(TypeDef type) {
			return GetExecMethod(deobfuscator, type);
		}

		static MethodDef GetExecMethod(MyDeobfuscator deobfuscator, TypeDef type) {
			MethodDef readMethod, execMethod;
			GetReadAndExecMethods(type, out readMethod, out execMethod);
			deobfuscator.Deobfuscate(execMethod);
			SimplifyInstructions(execMethod);
			return execMethod;
		}

		static SigCreator CreateSigCreator(CsvmInfo csvmInfo) {
			var creator = new SigCreator();

			creator.AddId(csvmInfo.LogicalOpShrUn, 1);
			creator.AddId(csvmInfo.LogicalOpShl, 2);
			creator.AddId(csvmInfo.LogicalOpShr, 3);
			creator.AddId(csvmInfo.LogicalOpAnd, 4);
			creator.AddId(csvmInfo.LogicalOpXor, 5);
			creator.AddId(csvmInfo.LogicalOpOr, 6);

			creator.AddId(csvmInfo.CompareLt, 7);
			creator.AddId(csvmInfo.CompareLte, 8);
			creator.AddId(csvmInfo.CompareGt, 9);
			creator.AddId(csvmInfo.CompareGte, 10);
			creator.AddId(csvmInfo.CompareEq, 11);
			creator.AddId(csvmInfo.CompareEqz, 12);

			creator.AddId(csvmInfo.ArithmeticSubOvfUn, 13);
			creator.AddId(csvmInfo.ArithmeticMulOvfUn, 14);
			creator.AddId(csvmInfo.ArithmeticRemUn, 15);
			creator.AddId(csvmInfo.ArithmeticRem, 16);
			creator.AddId(csvmInfo.ArithmeticDivUn, 17);
			creator.AddId(csvmInfo.ArithmeticDiv, 18);
			creator.AddId(csvmInfo.ArithmeticMul, 19);
			creator.AddId(csvmInfo.ArithmeticMulOvf, 20);
			creator.AddId(csvmInfo.ArithmeticSub, 21);
			creator.AddId(csvmInfo.ArithmeticSubOvf, 22);
			creator.AddId(csvmInfo.ArithmeticAddOvfUn, 23);
			creator.AddId(csvmInfo.ArithmeticAddOvf, 24);
			creator.AddId(csvmInfo.ArithmeticAdd, 25);

			creator.AddId(csvmInfo.UnaryNot, 26);
			creator.AddId(csvmInfo.UnaryNeg, 27);

			creator.AddId(csvmInfo.ArgsGet, 28);
			creator.AddId(csvmInfo.ArgsSet, 29);
			creator.AddId(csvmInfo.LocalsGet, 30);
			creator.AddId(csvmInfo.LocalsSet, 31);

			AddTypeId(creator, csvmInfo.LogicalOpShrUn, 32);
			AddTypeId(creator, csvmInfo.CompareLt, 33);
			AddTypeId(creator, csvmInfo.ArithmeticSubOvfUn, 34);
			AddTypeId(creator, csvmInfo.UnaryNot, 35);
			AddTypeId(creator, csvmInfo.ArgsGet, 36);

			return creator;
		}

		static void AddTypeId(SigCreator creator, MethodDef method, int id) {
			if (method != null)
				creator.AddId(method.DeclaringType, id);
		}

		static void GetReadAndExecMethods(TypeDef handler, out MethodDef readMethod, out MethodDef execMethod) {
			readMethod = execMethod = null;
			foreach (var method in handler.Methods) {
				if (!method.IsVirtual)
					continue;
				if (DotNetUtils.IsMethod(method, "System.Void", "(System.IO.BinaryReader)")) {
					if (readMethod != null)
						throw new ApplicationException("Found another read method");
					readMethod = method;
				}
				else if (!DotNetUtils.HasReturnValue(method) && method.MethodSig.GetParamCount() == 1) {
					if (execMethod != null)
						throw new ApplicationException("Found another execute method");
					execMethod = method;
				}
			}

			if (readMethod == null)
				throw new ApplicationException("Could not find read method");
			if (execMethod == null)
				throw new ApplicationException("Could not find execute method");
		}

		IEnumerable<TypeDef> GetVmHandlerTypes(TypeDef baseType) {
			foreach (var type in module.Types) {
				if (type.BaseType == baseType)
					yield return type;
			}
		}

		List<TypeDef> FindBasicVmHandlerTypes(CsvmInfo csvmInfo) {
			var list = new List<TypeDef>();
			if (csvmInfo.VmHandlerBaseType == null)
				return list;
			foreach (var type in module.Types) {
				if (list.Count == NUM_HANDLERS)
					break;
				if (type.BaseType == csvmInfo.VmHandlerBaseType)
					list.Add(type);
			}
			return list;
		}

		List<TypeDef> FindVmHandlerTypes() {
			var requiredFields = new string[] {
				null,
				"System.Collections.Generic.Dictionary`2<System.UInt16,System.Type>",
				"System.UInt16",
			};
			var cflowDeobfuscator = new CflowDeobfuscator();
			foreach (var type in module.Types) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;
				requiredFields[0] = type.FullName;
				var fieldTypes = new FieldTypes(type);
				if (!fieldTypes.All(requiredFields))
					continue;

				cflowDeobfuscator.Deobfuscate(cctor);
				var handlers = FindVmHandlerTypes(cctor);

				return handlers;
			}

			return null;
		}

		static List<TypeDef> FindVmHandlerTypes(MethodDef method) {
			var list = new List<TypeDef>();

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var type = instr.Operand as TypeDef;
				if (type == null)
					continue;

				list.Add(type);
			}

			return list;
		}
	}
}
