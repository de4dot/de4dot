using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Dotfuscator 
{
	/// <summary>
	///     remove stringbuilder crypter
	/// </summary>
	/// <code>
	///            int[] array2 = new int[]
	///            {
	///                -22,
	///                19,
	///                41,
	///                -36
	///            };
	///            StringBuilder stringBuilder2 = new StringBuilder();
	///            stringBuilder2.Append((char)(array2[0] + 98));
	///            stringBuilder2.Append((char)(array2[1] + 92));
	///            stringBuilder2.Append((char)(array2[2] + 56));
	///            stringBuilder2.Append((char)(array2[3] + 136));
	///            string text = stringBuilder2.ToString();
	/// </code>
	public class StringBuilderDecrypter {
		ModuleDefMD module;

		public StringBuilderDecrypter(ModuleDefMD module) => this.module = module;


		public void StringBuilderClean()
		{
			foreach (var type in module.GetTypes())
			{
				if (!type.HasMethods)
					continue;

				foreach (var method in type.Methods) 
				{
					CleanStringBuilder(method);

				}
			}
		}

		private void CleanStringBuilder(MethodDef method)
		{
			if (!method.HasBody)
				return;
			if (!method.Body.HasInstructions)
				return;
			if (method.Body.Instructions.Count < 4)
				return;
			if (method.Body.Variables.Count == 0)
				return;

			var instructions = method.Body.Instructions;

			GetStringBuilderFixIndexs(instructions, out var nopIdxs, out var ldstrIdxs);
			if (nopIdxs.Count > 0)
			{
				foreach (var idx in nopIdxs)
				{
					method.Body.Instructions[idx].OpCode  = OpCodes.Nop;
					method.Body.Instructions[idx].Operand = null;
				}
			}

			if (ldstrIdxs.Count > 0)
			{
				foreach (var idx in ldstrIdxs)
				{
					method.Body.Instructions[idx.Key].OpCode  = OpCodes.Ldstr;
					method.Body.Instructions[idx.Key].Operand = idx.Value;
				}
			}
		}


		private static void GetStringBuilderFixIndexs(IList<Instruction> instructions, out List<int> nopIdxs, out Dictionary<int, string> ldstrIdxs) {
			var insNoNops = instructions.Where(ins => ins.OpCode != OpCodes.Nop).ToList();

			nopIdxs = new List<int>();
			ldstrIdxs = new Dictionary<int, string>();

			for (var i = 1; i < insNoNops.Count; i++) {
				if (insNoNops[i - 1].IsLdcI4() &&
					insNoNops[i].OpCode == OpCodes.Newarr &&
					insNoNops[i].Operand is TypeRef typeRef &&
					typeRef.FullName == typeof(Int32).FullName) {
					int[] data = null;
					int index = 0;
					var arrLength = insNoNops[i - 1].GetLdcI4Value();
					if (arrLength == 1) {
						if (i + 6 < insNoNops.Count &&
							insNoNops[i + 6].OpCode == OpCodes.Newobj &&
							insNoNops[i + 6].Operand is MemberRef memberRef &&
							memberRef.GetDeclaringTypeFullName() == typeof(StringBuilder).FullName &&
							memberRef.Name == ".ctor" &&
							insNoNops[i + 3].IsLdcI4()) {
							data = new int[arrLength];
							data[0] = insNoNops[i + 3].GetLdcI4Value();

							index = TryGetStringBuilderAppendData(insNoNops, i + 7, data);
						}
					}
					if (arrLength == 2) {
						if (i + 10 < insNoNops.Count &&
							insNoNops[i + 10].OpCode == OpCodes.Newobj &&
							insNoNops[i + 10].Operand is MemberRef memberRef &&
							memberRef.GetDeclaringTypeFullName() == typeof(StringBuilder).FullName &&
							memberRef.Name == ".ctor" &&
							insNoNops[i + 3].IsLdcI4() &&
							insNoNops[i + 7].IsLdcI4()) {
							data = new int[arrLength];
							data[0] = insNoNops[i + 3].GetLdcI4Value();
							data[1] = insNoNops[i + 7].GetLdcI4Value();

							index = TryGetStringBuilderAppendData(insNoNops, i + 11, data);
						}
					}
					else {
						if (i + 2 < insNoNops.Count &&
							insNoNops[i + 2].OpCode == OpCodes.Ldtoken &&
							insNoNops[i + 2].Operand is FieldDef fieldDef &&
							fieldDef.InitialValue != null &&
							fieldDef.InitialValue.Length / 4 == arrLength &&
							insNoNops[i + 5].OpCode == OpCodes.Newobj &&
							insNoNops[i + 5].Operand is MemberRef memberRef &&
							memberRef.GetDeclaringTypeFullName() == typeof(StringBuilder).FullName &&
							memberRef.Name == ".ctor") {
							data = new int[arrLength];
							for (var j = 0; j < arrLength; j++) {
								data[j] = BitConverter.ToInt32(fieldDef.InitialValue, j * 4);
							}

							index = TryGetStringBuilderAppendData(insNoNops, i + 6, data);
						}
					}

					if (index != 0 && data != null) {
						var sb = new StringBuilder();
						foreach (var d in data) {
							sb.Append((char)d);
						}

						for (var j = i - 1; j < index; j++) {
							nopIdxs.Add(instructions.IndexOf(insNoNops[j]));
						}

						ldstrIdxs.Add(instructions.IndexOf(insNoNops[index]), sb.ToString());

						i = index;
					}
				}
			}
		}

		private static int TryGetStringBuilderAppendData(IList<Instruction> instructions, int index, int[] data)
		{
			var lenght = data.Length;

			if (instructions.Count                                                > index + lenght * 9 + 2 &&
			    instructions[index + 9 * lenght + 2].OpCode                       == OpCodes.Callvirt      &&
			    (instructions[index + 9 * lenght + 2].Operand as MemberRef)?.Name == "ToString")
			{
				if (instructions[index + 9 * lenght + 3].IsStloc() ||
				    instructions[index + 9 * lenght + 3].OpCode == OpCodes.Pop)
				{
					for (var j = 0; j < lenght; j++)
					{
						var insNoNop = instructions[index + j * 9 + 5];
						if (insNoNop.IsLdcI4())
						{
							data[j] += insNoNop.GetLdcI4Value();
						}
						else
						{
							return 0;
						}
					}

					return index + 9 * lenght + 2;
				}
			}

			return 0;
		}

	}


}
