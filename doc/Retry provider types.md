# Retry provider types

Good question. The **exponential backoff is not specified by SqlRetryLogicOption itself**.  
It is specified here:  
  
var retryProvider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);  
  
The word ****Exponential**** in:  
  
CreateExponentialRetryProvider(...)  
  
is what tells SqlClient to use exponential backoff.  
There are different providers available:  
  
SqlConfigurableRetryFactory.CreateNoneRetryProvider()  
  
SqlConfigurableRetryFactory.CreateFixedRetryProvider(...)  
  
SqlConfigurableRetryFactory.CreateIncrementalRetryProvider(...)  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
Each provider interprets the same SqlRetryLogicOption differently.  
## Fixed Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateFixedRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 2 sec  
Retry 3 -> 2 sec  
Retry 4 -> 2 sec  
  
## Incremental Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateIncrementalRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 4 sec  
Retry 3 -> 6 sec  
Retry 4 -> 8 sec  
  
## Exponential Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 4 sec  
Retry 3 -> 8 sec  
Retry 4 -> 16 sec  
  
The built-in providers also add some random jitter to avoid retry storms.  
## Code Review Tip  
For Azure SQL, the specific line I would look for is:  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
If I see:  
  
CreateFixedRetryProvider(...)  
  
I would ask:  
Why fixed delay instead of exponential backoff for transient Azure SQL failures?  
For most Azure SQL production workloads, **ExponentialRetryProvider** is generally the preferred choice because it backs off more aggressively during throttling, failovers, and temporary outages.
