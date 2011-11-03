/*
    Copyright (C) 2011 de4dot@gmail.com

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
using de4dot.blocks;

namespace de4dot.renamer {
	class MemberRenameState {
		public VariableNameState variableNameState;
		public Dictionary<PropertyReferenceKey, PropertyRef> properties = new Dictionary<PropertyReferenceKey, PropertyRef>();
		public Dictionary<EventReferenceKey, EventRef> events = new Dictionary<EventReferenceKey, EventRef>();
		public Dictionary<MethodReferenceKey, MethodRef> methods = new Dictionary<MethodReferenceKey, MethodRef>();

		public MemberRenameState()
			: this(null) {
		}

		public MemberRenameState(VariableNameState variableNameState) {
			this.variableNameState = variableNameState;
		}

		// Used to merge all renamed interface props/events/methods
		public void mergeRenamed(MemberRenameState other) {
			foreach (var key in other.properties.Keys)
				add(properties, key, other.properties[key]);
			foreach (var key in other.events.Keys)
				add(events, key, other.events[key]);
			foreach (var key in other.methods.Keys)
				add(methods, key, other.methods[key]);
		}

		public PropertyRef get(PropertyRef p) {
			return get(properties, new PropertyReferenceKey(p.PropertyReference));
		}

		public EventRef get(EventRef e) {
			return get(events, new EventReferenceKey(e.EventReference));
		}

		public MethodRef get(MethodRef m) {
			return get(methods, new MethodReferenceKey(m.MethodReference));
		}

		// Returns null if not found
		D get<K, D>(Dictionary<K, D> dict, K key) where D : class {
			D value;
			if (dict.TryGetValue(key, out value))
				return value;
			return null;
		}

		public void add(PropertyRef p) {
			add(properties, new PropertyReferenceKey(p.PropertyReference), p);
		}

		public void add(EventRef e) {
			add(events, new EventReferenceKey(e.EventReference), e);
		}

		public void add(MethodRef m) {
			add(methods, new MethodReferenceKey(m.MethodReference), m);
		}

		void add<K, D>(Dictionary<K, D> dict, K key, D d) {
			dict[key] = d;
		}

		public MemberRenameState clone() {
			var rv = new MemberRenameState(variableNameState == null ? null : variableNameState.clone());
			rv.properties = new Dictionary<PropertyReferenceKey, PropertyRef>(properties);
			rv.events = new Dictionary<EventReferenceKey, EventRef>(events);
			rv.methods = new Dictionary<MethodReferenceKey, MethodRef>(methods);
			return rv;
		}

		public MemberRenameState cloneVariables() {
			var rv = new MemberRenameState(variableNameState == null ? null : variableNameState.clone());
			rv.properties = properties;
			rv.events = events;
			rv.methods = methods;
			return rv;
		}
	}
}
