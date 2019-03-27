# &#9084; Resin

## Introduction

Resin is a remote

- vector database where the key is a 64-bit vector that may or may not translate into a string  (it's up to you) 
and the payload is a list of Int64's. What the payload translates into is also your choice. At querying time 
the key is either a fixed length Int64 or a variable length query expression. 
Each node in such an expression tree carries a fixed length key and also define either an AND, OR or NOT set operation.
- [VectorNode](https://github.com/kreeben/resin/blob/master/src/Sir.Store/VectorNode.cs). 
With it you can define and then traverse a 64-bit wide vector space containing anything that is willing to be 
constrained by it. The payload of each node is a list of Int64's.

One application of such an architecture is a language model framework. Another is a string database. A third is 
a kind of search engine that lets you talk to your data using natural language or structured queries. 
Resin is at least those three things but probably more.

You can install it in the cloud, distributed onto many machines, each one carrying collections of collections and 
indices for each (analyzed) key in each collection while running one central postings server. 
Or you can run it on your laptop.


Here is a non-exhaustive list of features.

### Features

- Create, append to and query document collections of any format (JSON format included out-of-the-box)
- Query in natural language or structured
- Create 1-n relationships, e.g. one utterance to many documents
- Create n-1 relationships, e.g. many utterances to one intent
- Create intent-based applications
- Create embeddings/language models from collections
- Build custom models in new vector spaces, based on previous models
- Plug in your own reader/writer filters
- Build digital conversationalists (e.g. chat bots, search engines, digital assistants)
- All features are embeddable (by using Resin as a library) but also distributable (by talking to Resin over HTTP)

### Resin includes a web GUI where you can

- query collections of documents naturally or structured
- create new collections from slices of existing collections, slices that are defined by queries

### and a HTTP API that you can use to

- create new document collections
- query naturally/structured over HTTP with content type negotiation

### and a plugin system for read/write filters.

[IReader](https://github.com/kreeben/resin/blob/master/src/Sir/IReader.cs) and 
[IWriter](https://github.com/kreeben/resin/blob/master/src/Sir/IWriter.cs).

## Natural and structured querying

To find

	documents where title is Rambo or First Blood but only if the genre isn't books
	
you can use natural language or structured:

	+(title:rambo title:first blood) -(genre:books)

## Bag-of-characters ("BOC") model (included out-of-the-box)

Resin creates a vector space of words embedded as bags-of-characters. 
This type of embedding was chosen for its encoding speed and low CPU pressure at querying time.

With all embeddings aggregated as a [VectorNode](https://github.com/kreeben/resin/blob/master/src/Sir.Store/VectorNode.cs) 
graph you have a model that form clusters of documents that share similar words. 

Natural language queries are parsed into expression trees with nodes of bags-of-characters that represent words  
and a AND, OR or NOT set operation. 
The expression is serialized and executed (reduced) on a remote server, producing a set of IDs of documents that came from as 
many clusters as there are (distinct) additative terms in the query.  

That set is sorted by score and a window defined by skip and take parameters are returned to the orchestrating server, 
who materializes the list of document IDs, i.e. reads and returns to the client a windows of those documents, 
formatted according to the HTTP client's "Accept" header.

## Document model (not production-ready)

The model is a graph of documents embedded as bags-of-words. Documents gather around topics. 

Natural language queries are parsed into a tree of document sized vectors. 
A cluster of documents is located by reducing the clause vectors to a single document 
by using vector addition/subtraction and by navigating the index graph by evaluating 
the cos angle between the query and the clusters. The end-result of the scan is a cluster ID 
that also corresponds to a postings list ID. If the topic is a big one, the result set will be large. 
If you've managed to pinpoint a shallow cluster your result set will be smaller.

The reason for creating such a document model is to represent each document once per index 
instead of once per term as is the case with the BOC model, making it possible to find a topic with a single scan.

## Install

Download a clone of this repository, launch the solution in Visual Studio to build and publish it. 
Then create a .Net Core IIS site that points to [path_of_clone]/src/publish. 
Make sure the app pool type is "unmanaged".  

Read below how to create document collections. Use your favorite HTTP client to create a collection 
from an array of JSON documents. Read on to learn about querying your data, how to slice and then re-model it.

## Create your own collections

To create collections from your favorite data you may host one of these servers yourself, privately or publicly, 
or you can use a [free search cloud](https://didyougogo.com).

#### POST a JSON document to the WRITE endpoint

	HTTPS POST didyougogo.com/io/[collection_name]
	Content-Type:application/json
	[
		{
			"field1":"value1"
		},
		{
			"field1":"value2"
		}
	]
####	Server should respond with a list of document IDs:

	[
		1,
		2
	]

#### GET document by ID

	HTTPS GET didyougogo.com/io/[collection_name]?id=[document_id]
	Accept:application/json

#### Query collection with natural language through the API

	HTTPS GET didyougogo.com/io/[collection_name]?q=[phrase-or-term]&fields=title&skip=0&take=10  
	Accept:application/json

#### Query collection with query language through the API

	HTTPS GET didyougogo.com/io/[collection_name]?&qf=[scoped_query]&skip=0&take=10  
	Accept:application/json

#### Query GUI

	HTTPS GET didyougogo.com/?q=[phrase-or-term-query]&fields=title&skip=0&take=10&collection=[collection_name]

#### Slice collections using structured queries with the advanced query parser

	HTTPS GET didyougogo.com/queryparser/?q=[phrase-or-term-query]&qf=[scoped_query]&fields=title&skip=0&take=10&collection=[collection_name]

## HTTP reader/writer micro-service framework.
Create distributable readers and writers. Splits a problem into two. 
https://github.com/kreeben/resin/tree/master/src/Sir.HttpServer

## A key/value writer and map/reduce node. 
Execute AND, OR and NOT set operations over local lists of Int64's.  
https://github.com/kreeben/resin/tree/master/src/Sir.Postings

## Document writer and map/reduce orchestrator. 
Database and search index. Orchestrates remote set operations.   
https://github.com/kreeben/resin/tree/master/src/Sir.Store

## Roadmap

- [x] v0.1a - bag-of-characters term vector space language model
- [x] v0.2a - HTTP API comprised of distributable search microservices
- [x] v0.3a - boolean query language with support for AND ('+'), OR (' '), NOT ('-') and scope ('(', ')').
- [ ] v0.4b - bag-of-words document vector space language model
- [ ] v0.5 - semantic language model
- [ ] v0.6 - local join between collections
- [ ] v0.7 - private online collections
- [ ] v0.8 - join (orchestrate) over private/public collections
- [ ] v0.9 - add support for voice models
- [ ] v1.0 - add support for image models
- [ ] v2.0 - implement text/image-model-to-voice
- [ ] v2.1 - implement text/voice-model-to-image
- [ ] v2.2 - implement image/voice-model-to-text
- [ ] v3.0 - AI
