﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Sir
{
    /// <summary>
    /// Read data from a collection.
    /// </summary>
    public interface IReader : IPlugin
    {
       Task<ResultModel> Read(string collectionId, HttpRequest request);
    }
}
