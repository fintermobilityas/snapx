using ServiceStack;
using System;

namespace snapx.Api
{
    [Route("/lock")]
    [Route("/lock/{Name}/{Duration}")]
    public class Lock : IReturn<string>
    {
        public string Name { get; set; }
        public TimeSpan Duration { get; set; }
    }

    [Route("/renewlock")]
    [Route("/renew/{Name}/{Duration}")]
    public class RenewLock : IReturn<bool>
    {
        public string Name { get; set; }
        public string Challenge { get; set; }
    }

    [Route("/unlock")]
    [Route("/unlock/{Name}/{Challenge}")]
    public class Unlock : IReturn<bool>
    {
        public string Name { get; set; }
        public string Challenge { get; set; }
        public TimeSpan? BreakPeriod { get; set; }
    }
}
