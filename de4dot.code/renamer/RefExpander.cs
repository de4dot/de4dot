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

using Mono.Cecil;

namespace de4dot.renamer {
	abstract class RefExpander {
		protected GenericInstanceType git;
		bool modified = false;

		public RefExpander(GenericInstanceType git) {
			this.git = git;
		}

		protected void checkModified(object a, object b) {
			if (!ReferenceEquals(a, b))
				modified = true;
		}

		protected MethodReference expandMethodReference(MethodReference methodReference) {
			var mr = new MethodReferenceExpander(methodReference, git).expand();
			checkModified(methodReference, mr);
			return mr;
		}

		protected EventReference expandEventReference(EventReference eventReference) {
			var er = new EventReferenceExpander(eventReference, git).expand();
			checkModified(eventReference, er);
			return er;
		}

		protected PropertyReference expandPropertyReference(PropertyReference propertyReference) {
			var pr = new PropertyReferenceExpander(propertyReference, git).expand();
			checkModified(propertyReference, pr);
			return pr;
		}

		protected T getResult<T>(T orig, T expanded) {
			return modified ? expanded : orig;
		}
	}

	class GenericMethodRefExpander : RefExpander {
		MethodRef methodRef;

		public GenericMethodRefExpander(MethodRef methodRef, GenericInstanceType git)
			: base(git) {
			this.methodRef = methodRef;
		}

		public MethodRef expand() {
			var newMethodRef = new MethodRef(expandMethodReference(methodRef.MethodReference), methodRef.Owner, methodRef.Index);
			newMethodRef.NewName = methodRef.NewName;
			return getResult(methodRef, newMethodRef);
		}
	}

	class GenericEventRefExpander : RefExpander {
		EventRef eventRef;

		public GenericEventRefExpander(EventRef eventRef, GenericInstanceType git)
			: base(git) {
			this.eventRef = eventRef;
		}

		public EventRef expand() {
			var newEventRef = new EventRef(expandEventReference(eventRef.EventReference), eventRef.Owner, eventRef.Index);
			newEventRef.NewName = eventRef.NewName;
			return getResult(eventRef, newEventRef);
		}
	}

	class GenericPropertyRefExpander : RefExpander {
		PropertyRef propRef;

		public GenericPropertyRefExpander(PropertyRef propRef, GenericInstanceType git)
			: base(git) {
			this.propRef = propRef;
		}

		public PropertyRef expand() {
			var newPropRef = new PropertyRef(expandPropertyReference(propRef.PropertyReference), propRef.Owner, propRef.Index);
			newPropRef.NewName = propRef.NewName;
			return getResult(propRef, newPropRef);
		}
	}
}
