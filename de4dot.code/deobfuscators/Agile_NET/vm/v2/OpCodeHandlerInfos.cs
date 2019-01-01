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
using System.IO;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	static class OpCodeHandlerInfos {
		public static void Write(BinaryWriter writer, List<MethodSigInfo> handlerInfos) {
			writer.Write(1);
			writer.Write(handlerInfos.Count);
			foreach (var handler in handlerInfos) {
				writer.Write((int)handler.TypeCode);
				writer.Write(handler.BlockSigInfos.Count);
				foreach (var info in handler.BlockSigInfos) {
					writer.Write(info.Targets.Count);
					foreach (var target in info.Targets)
						writer.Write(target);
					writer.Write(info.Hashes.Count);
					foreach (var hash in info.Hashes)
						writer.Write((uint)hash);
					writer.Write(info.HasFallThrough);
					writer.Write(info.EndsInRet);
				}
			}
		}

		public static List<MethodSigInfo> Read(BinaryReader reader) {
			if (reader.ReadInt32() != 1)
				throw new InvalidDataException();
			int numHandlers = reader.ReadInt32();
			var list = new List<MethodSigInfo>(numHandlers);
			for (int i = 0; i < numHandlers; i++) {
				var typeCode = (HandlerTypeCode)reader.ReadInt32();
				int numBlocks = reader.ReadInt32();
				var blocks = new List<BlockSigInfo>(numBlocks);
				for (int j = 0; j < numBlocks; j++) {
					int numTargets = reader.ReadInt32();
					var targets = new List<int>(numTargets);
					for (int k = 0; k < numTargets; k++)
						targets.Add(reader.ReadInt32());
					var numHashes = reader.ReadInt32();
					var hashes = new List<BlockElementHash>(numHashes);
					for (int k = 0; k < numHashes; k++)
						hashes.Add((BlockElementHash)reader.ReadInt32());
					var block = new BlockSigInfo(hashes, targets) {
						HasFallThrough = reader.ReadBoolean(),
						EndsInRet = reader.ReadBoolean(),
					};
					blocks.Add(block);
				}
				list.Add(new MethodSigInfo(blocks, typeCode));
			}
			return list;
		}

		public static readonly IList<MethodSigInfo>[] HandlerInfos = new IList<MethodSigInfo>[] {
			ReadOpCodeHandlerInfos(CsvmResources.CSVM1),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM2),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM3),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM4),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM5),
			ReadOpCodeHandlerInfos(CsvmResources.CSVM6),

		};

		static IList<MethodSigInfo> ReadOpCodeHandlerInfos(byte[] data) =>
			OpCodeHandlerInfos.Read(new BinaryReader(new MemoryStream(data)));
	}
}
