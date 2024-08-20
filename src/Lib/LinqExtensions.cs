using System.Collections.Generic;

namespace System.Linq;
					
static class LinqExtensions {
    ///<summary>Converts sequence to single object</summary>
	public static TResult To<TSource, TResult>(
            this IEnumerable<TSource> source, Func<IEnumerable<TSource>, TResult> convert) {
		return convert(source);
	}
}