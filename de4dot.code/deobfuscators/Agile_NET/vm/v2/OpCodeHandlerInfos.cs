/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using System.IO;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	static class OpCodeHandlerInfos {
		enum OpCodeHandlersFileVersion : int {
			V1 = 1,
		}

		public static void Write(BinaryWriter writer, IList<OpCodeHandlerInfo> handlerInfos) {
			WriteV1(writer, handlerInfos);
		}

		public static void WriteV1(BinaryWriter writer, IList<OpCodeHandlerInfo> handlerInfos) {
			writer.Write((int)OpCodeHandlersFileVersion.V1);
			writer.Write(handlerInfos.Count);
			foreach (var handler in handlerInfos) {
				writer.Write((int)handler.TypeCode);
				var infos = handler.ExecSig.BlockInfos;
				writer.Write(infos.Count);
				foreach (var info in infos) {
					if (info.Hash == null)
						writer.Write(0);
					else {
						writer.Write(info.Hash.Length);
						writer.Write(info.Hash);
					}
					writer.Write(info.Targets.Count);
					foreach (var target in info.Targets)
						writer.Write(target);
				}
			}
		}

		public static List<OpCodeHandlerInfo> Read(BinaryReader reader) {
			switch ((OpCodeHandlersFileVersion)reader.ReadInt32()) {
			case OpCodeHandlersFileVersion.V1: return ReadV1(reader);
			default: throw new ApplicationException("Invalid file version");
			}
		}

		static List<OpCodeHandlerInfo> ReadV1(BinaryReader reader) {
			int numHandlers = reader.ReadInt32();
			var list = new List<OpCodeHandlerInfo>(numHandlers);
			for (int i = 0; i < numHandlers; i++) {
				var typeCode = (HandlerTypeCode)reader.ReadInt32();
				int numInfos = reader.ReadInt32();
				var sigInfo = new MethodSigInfo();
				for (int j = 0; j < numInfos; j++) {
					var info = new BlockInfo();

					info.Hash = reader.ReadBytes(reader.ReadInt32());
					if (info.Hash.Length == 0)
						info.Hash = null;

					int numTargets = reader.ReadInt32();
					for (int k = 0; k < numTargets; k++)
						info.Targets.Add(reader.ReadInt32());

					sigInfo.BlockInfos.Add(info);
				}

				list.Add(new OpCodeHandlerInfo(typeCode, sigInfo));
			}
			return list;
		}

		public static readonly IList<OpCodeHandlerInfo>[] HandlerInfos = new IList<OpCodeHandlerInfo>[] {
			ReadOpCodeHandlerInfos(CsvmResources.CSVM1_v2),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM2_v2),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM3_v2),
		};

		static IList<OpCodeHandlerInfo> ReadOpCodeHandlerInfos(byte[] data) {
			return OpCodeHandlerInfos.Read(new BinaryReader(new MemoryStream(data)));
		}
	}
}
