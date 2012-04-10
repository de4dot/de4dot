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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	class OpCodeHandlerSigInfo {
		public object[] RequiredFieldTypes { get; set; }
		public string[] ExecuteMethodLocals { get; set; }
		public int? ExecuteMethodThrows { get; set; }
		public int? NumStaticMethods { get; set; }
		public int? NumInstanceMethods { get; set; }
		public int? NumVirtualMethods { get; set; }
		public int? NumCtors { get; set; }
	}

	class VmOpCodeHandlerDetector {
		ModuleDefinition module;
		static readonly OpCodeHandler[] opCodeHandlerDetectors = new OpCodeHandler[] {
			new ArithmeticOpCodeHandler(),
			new ArrayOpCodeHandler(),
			new BoxOpCodeHandler(),
			new CallOpCodeHandler(),
			new CastOpCodeHandler(),
			new CompareOpCodeHandler(),
			new ConvertOpCodeHandler(),
			new DupPopOpCodeHandler(),
			new ElemOpCodeHandler(),
			new EndfinallyOpCodeHandler(),
			new FieldOpCodeHandler(),
			new InitobjOpCodeHandler(),
			new LdLocalArgOpCodeHandler(),
			new LdLocalArgAddrOpCodeHandler(),
			new LdelemaOpCodeHandler(),
			new LdlenOpCodeHandler(),
			new LdobjOpCodeHandler(),
			new LdstrOpCodeHandler(),
			new LdtokenOpCodeHandler(),
			new LeaveOpCodeHandler(),
			new LoadConstantOpCodeHandler(),
			new LoadFuncOpCodeHandler(),
			new LogicalOpCodeHandler(),
			new NopOpCodeHandler(),
			new RetOpCodeHandler(),
			new RethrowOpCodeHandler(),
			new StLocalArgOpCodeHandler(),
			new StobjOpCodeHandler(),
			new SwitchOpCodeHandler(),
			new ThrowOpCodeHandler(),
			new UnaryOpCodeHandler(),
		};
		List<OpCodeHandler> opCodeHandlers;

		public List<OpCodeHandler> Handlers {
			get { return opCodeHandlers; }
		}

		public VmOpCodeHandlerDetector(ModuleDefinition module) {
			this.module = module;
		}

		public void findHandlers() {
			if (opCodeHandlers != null)
				return;
			var vmHandlerTypes = findVmHandlerTypes();
			if (vmHandlerTypes == null)
				throw new ApplicationException("Could not find CSVM opcode handler types");

			detectHandlers(vmHandlerTypes);
		}

		List<TypeDefinition> findVmHandlerTypes() {
			var requiredFields = new string[] {
				null,
				"System.Collections.Generic.Dictionary`2<System.UInt16,System.Type>",
				"System.UInt16",
			};
			var cflowDeobfuscator = new CflowDeobfuscator(new NoMethodInliner());
			foreach (var type in module.Types) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;
				requiredFields[0] = type.FullName;
				if (!new FieldTypes(type).exactly(requiredFields))
					continue;

				cflowDeobfuscator.deobfuscate(cctor);
				var handlers = findVmHandlerTypes(cctor);
				if (handlers.Count != 31)
					continue;

				return handlers;
			}

			return null;
		}

		static List<TypeDefinition> findVmHandlerTypes(MethodDefinition method) {
			var list = new List<TypeDefinition>();

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var type = instr.Operand as TypeDefinition;
				if (type == null)
					continue;

				list.Add(type);
			}

			return list;
		}

		void detectHandlers(List<TypeDefinition> handlerTypes) {
			opCodeHandlers = new List<OpCodeHandler>();
			var detected = new List<OpCodeHandler>();
			foreach (var handlerType in handlerTypes) {
				var info = new UnknownHandlerInfo(handlerType);
				detected.Clear();
				foreach (var opCodeHandler in opCodeHandlerDetectors) {
					if (opCodeHandler.detect(info))
						detected.Add(opCodeHandler);
				}
				if (detected.Count != 1)
					throw new ApplicationException("Could not detect VM opcode handler");
				opCodeHandlers.Add(detected[0]);
			}
			if (new List<OpCodeHandler>(Utils.unique(opCodeHandlers)).Count != opCodeHandlers.Count)
				throw new ApplicationException("Could not detect all VM opcode handlers");
		}
	}
}
