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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code {
	public class MethodPrinter {
		LoggerEvent loggerEvent;
		IList<Instruction> allInstructions;
		IList<ExceptionHandler> allExceptionHandlers;
		Dictionary<Instruction, bool> targets = new Dictionary<Instruction, bool>();
		Dictionary<Instruction, string> labels = new Dictionary<Instruction, string>();

		class ExInfo {
			public List<ExceptionHandler> tryStarts = new List<ExceptionHandler>();
			public List<ExceptionHandler> tryEnds = new List<ExceptionHandler>();
			public List<ExceptionHandler> filterStarts = new List<ExceptionHandler>();
			public List<ExceptionHandler> handlerStarts = new List<ExceptionHandler>();
			public List<ExceptionHandler> handlerEnds = new List<ExceptionHandler>();
		}
		Dictionary<Instruction, ExInfo> exInfos = new Dictionary<Instruction, ExInfo>();
		ExInfo lastExInfo;

		public void Print(LoggerEvent loggerEvent, IList<Instruction> allInstructions, IList<ExceptionHandler> allExceptionHandlers) {
			try {
				this.loggerEvent = loggerEvent;
				this.allInstructions = allInstructions;
				this.allExceptionHandlers = allExceptionHandlers;
				lastExInfo = new ExInfo();
				Print();
			}
			finally {
				this.allInstructions = null;
				this.allExceptionHandlers = null;
				targets.Clear();
				labels.Clear();
				exInfos.Clear();
				lastExInfo = null;
			}
		}

		void InitTargets() {
			foreach (var instr in allInstructions) {
				switch (instr.OpCode.OperandType) {
				case OperandType.ShortInlineBrTarget:
				case OperandType.InlineBrTarget:
					SetTarget(instr.Operand as Instruction);
					break;

				case OperandType.InlineSwitch:
					foreach (var targetInstr in (Instruction[])instr.Operand)
						SetTarget(targetInstr);
					break;
				}
			}

			foreach (var ex in allExceptionHandlers) {
				SetTarget(ex.TryStart);
				SetTarget(ex.TryEnd);
				SetTarget(ex.FilterStart);
				SetTarget(ex.HandlerStart);
				SetTarget(ex.HandlerEnd);
			}

			var sortedTargets = new List<Instruction>(targets.Keys);
			sortedTargets.Sort((a, b) => a.Offset.CompareTo(b.Offset));
			for (int i = 0; i < sortedTargets.Count; i++)
				labels[sortedTargets[i]] = string.Format("label_{0}", i);
		}

		void SetTarget(Instruction instr) {
			if (instr != null)
				targets[instr] = true;
		}

		void InitExHandlers() {
			foreach (var ex in allExceptionHandlers) {
				if (ex.TryStart != null) {
					GetExInfo(ex.TryStart).tryStarts.Add(ex);
					GetExInfo(ex.TryEnd).tryEnds.Add(ex);
				}
				if (ex.FilterStart != null)
					GetExInfo(ex.FilterStart).filterStarts.Add(ex);
				if (ex.HandlerStart != null) {
					GetExInfo(ex.HandlerStart).handlerStarts.Add(ex);
					GetExInfo(ex.HandlerEnd).handlerEnds.Add(ex);
				}
			}
		}

		ExInfo GetExInfo(Instruction instruction) {
			if (instruction == null)
				return lastExInfo;
			ExInfo exInfo;
			if (!exInfos.TryGetValue(instruction, out exInfo))
				exInfos[instruction] = exInfo = new ExInfo();
			return exInfo;
		}

		void Print() {
			InitTargets();
			InitExHandlers();

			Logger.Instance.Indent();
			foreach (var instr in allInstructions) {
				if (targets.ContainsKey(instr)) {
					Logger.Instance.DeIndent();
					Logger.Log(loggerEvent, "{0}:", GetLabel(instr));
					Logger.Instance.Indent();
				}
				ExInfo exInfo;
				if (exInfos.TryGetValue(instr, out exInfo))
					PrintExInfo(exInfo);
				var instrString = instr.OpCode.Name;
				var operandString = GetOperandString(instr);
				var memberRef = instr.Operand as ITokenOperand;
				if (operandString == "")
					Logger.Log(loggerEvent, "{0}", instrString);
				else if (memberRef != null)
					Logger.Log(loggerEvent, "{0,-9} {1} // {2:X8}", instrString, Utils.RemoveNewlines(operandString), memberRef.MDToken.ToUInt32());
				else
					Logger.Log(loggerEvent, "{0,-9} {1}", instrString, Utils.RemoveNewlines(operandString));
			}
			PrintExInfo(lastExInfo);
			Logger.Instance.DeIndent();
		}

		string GetOperandString(Instruction instr) {
			if (instr.Operand is Instruction)
				return GetLabel((Instruction)instr.Operand);
			else if (instr.Operand is Instruction[]) {
				var sb = new StringBuilder();
				var targets = (Instruction[])instr.Operand;
				for (int i = 0; i < targets.Length; i++) {
					if (i > 0)
						sb.Append(',');
					sb.Append(GetLabel(targets[i]));
				}
				return sb.ToString();
			}
			else if (instr.Operand is string)
				return Utils.ToCsharpString((string)instr.Operand);
			else if (instr.Operand is Parameter) {
				var arg = (Parameter)instr.Operand;
				var s = InstructionPrinter.GetOperandString(instr);
				if (s != "")
					return s;
				return string.Format("<arg_{0}>", arg.Index);
			}
			else
				return InstructionPrinter.GetOperandString(instr);
		}

		void PrintExInfo(ExInfo exInfo) {
			Logger.Instance.DeIndent();
			foreach (var ex in exInfo.tryStarts)
				Logger.Log(loggerEvent, "// try start: {0}", GetExceptionString(ex));
			foreach (var ex in exInfo.tryEnds)
				Logger.Log(loggerEvent, "// try end: {0}", GetExceptionString(ex));
			foreach (var ex in exInfo.filterStarts)
				Logger.Log(loggerEvent, "// filter start: {0}", GetExceptionString(ex));
			foreach (var ex in exInfo.handlerStarts)
				Logger.Log(loggerEvent, "// handler start: {0}", GetExceptionString(ex));
			foreach (var ex in exInfo.handlerEnds)
				Logger.Log(loggerEvent, "// handler end: {0}", GetExceptionString(ex));
			Logger.Instance.Indent();
		}

		string GetExceptionString(ExceptionHandler ex) {
			var sb = new StringBuilder();
			if (ex.TryStart != null)
				sb.Append(string.Format("TRY: {0}-{1}", GetLabel(ex.TryStart), GetLabel(ex.TryEnd)));
			if (ex.FilterStart != null)
				sb.Append(string.Format(", FILTER: {0}", GetLabel(ex.FilterStart)));
			if (ex.HandlerStart != null)
				sb.Append(string.Format(", HANDLER: {0}-{1}", GetLabel(ex.HandlerStart), GetLabel(ex.HandlerEnd)));
			sb.Append(string.Format(", TYPE: {0}", ex.HandlerType));
			if (ex.CatchType != null)
				sb.Append(string.Format(", CATCH: {0}", ex.CatchType));
			return sb.ToString();
		}

		string GetLabel(Instruction instr) {
			if (instr == null)
				return "<end>";
			return labels[instr];
		}
	}
}
