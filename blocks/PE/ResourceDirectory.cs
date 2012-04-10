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

using System.Collections.Generic;

namespace de4dot.PE {
	public class ResourceDirectory : ResourceDirectoryEntry {
		Resources resources;
		int offset;
		List<ResourceData> resourceDataList = new List<ResourceData>();
		List<ResourceDirectory> resourceDirectoryList = new List<ResourceDirectory>();

		public List<ResourceData> Data {
			get { return resourceDataList; }
		}

		public List<ResourceDirectory> Directories {
			get { return resourceDirectoryList; }
		}

		public ResourceDirectory(int id, Resources resources, int offset)
			: base(id) {
			init(resources, offset);
		}

		public ResourceDirectory(string name, Resources resources, int offset)
			: base(name) {
			init(resources, offset);
		}

		void init(Resources resources, int offset) {
			this.resources = resources;
			this.offset = offset;
			initializeEntries();
		}

		public ResourceDirectory getDirectory(int id) {
			return find(resourceDirectoryList, id);
		}

		public ResourceDirectory getDirectory(string name) {
			return find(resourceDirectoryList, name);
		}

		public ResourceData getData(int id) {
			return find(resourceDataList, id);
		}

		public ResourceData getData(string name) {
			return find(resourceDataList, name);
		}

		void initializeEntries() {
			if (offset < 0)
				return;
			if (!resources.isSizeAvailable(offset, 16))
				return;
			if (!resources.seek(offset + 12))
				return;

			int named = resources.readUInt16();
			int ids = resources.readUInt16();
			int total = named + ids;
			if (!resources.isSizeAvailable(total * 8))
				return;

			for (int i = 0, entryOffset = offset + 16; i < total; i++, entryOffset += 8) {
				resources.seek(entryOffset);
				uint nameOrId = resources.readUInt32();
				uint dataOrDirectory = resources.readUInt32();

				string name = null;
				int id = -1;
				if ((nameOrId & 0x80000000) != 0) {
					name = resources.readString((int)(nameOrId & 0x7FFFFFFF));
					if (name == null)
						break;
				}
				else
					id = (int)nameOrId;

				if ((dataOrDirectory & 0x80000000) == 0) {
					if (!resources.seek((int)dataOrDirectory))
						break;
					if (!resources.isSizeAvailable(16))
						break;
					uint dataRva = resources.readUInt32();
					uint dataSize = resources.readUInt32();
					if (name == null)
						resourceDataList.Add(new ResourceData(id, dataRva, dataSize));
					else
						resourceDataList.Add(new ResourceData(name, dataRva, dataSize));
				}
				else {
					int directoryOffset = (int)(dataOrDirectory & 0x7FFFFFFF);
					if (name == null)
						resourceDirectoryList.Add(new ResourceDirectory(id, resources, directoryOffset));
					else
						resourceDirectoryList.Add(new ResourceDirectory(name, resources, directoryOffset));
				}
			}
		}

		public override string ToString() {
			return string.Format("OFS: {0:X8}, NAME: {1}", offset, getName());
		}
	}
}
