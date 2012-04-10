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
	public abstract class ResourceDirectoryEntry {
		protected readonly string name;
		protected readonly int id;

		public bool HasStringName {
			get { return name != null; }
		}

		public bool HasId {
			get { return !HasStringName; }
		}

		public string Name {
			get { return name; }
		}

		public int Id {
			get { return id; }
		}

		public ResourceDirectoryEntry(int id) {
			this.name = null;
			this.id = id;
		}

		public ResourceDirectoryEntry(string name) {
			this.name = name;
			this.id = -1;
		}

		protected string getName() {
			return HasStringName ? name : id.ToString();
		}

		protected static T find<T>(IEnumerable<T> list, int id) where T : ResourceDirectoryEntry {
			foreach (var dirEntry in list) {
				if (dirEntry.HasId && dirEntry.Id == id)
					return dirEntry;
			}
			return null;
		}

		protected static T find<T>(IEnumerable<T> list, string name) where T : ResourceDirectoryEntry {
			foreach (var dirEntry in list) {
				if (dirEntry.HasStringName && dirEntry.Name == name)
					return dirEntry;
			}
			return null;
		}
	}
}
