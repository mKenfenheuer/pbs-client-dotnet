using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Internal
{
    internal static class TaskExtensions
    {

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
            if (completedTask == task)
            {
                return await task;  // Very important in order to propagate exceptions
            }
            else
            {
                throw new TimeoutException("The operation has timed out.");
            }
        }
    }

}
