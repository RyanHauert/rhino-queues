using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Isam.Esent.Interop;
using Rhino.Queues.Exceptions;

namespace Rhino.Queues.Storage
{
    public abstract class AbstractActions : IDisposable
    {
        protected JET_DBID dbid;
        protected Table queues;
        protected Table recovery;
        protected Dictionary<string, JET_COLUMNID> recoveryColumns;
        protected Dictionary<string, JET_COLUMNID> outgoingColumns;
        protected Dictionary<string, JET_COLUMNID> outgoingHistoryColumns;
        protected Session session;
        protected Transaction transaction;
        protected Table txs;
        protected Table outgoing;
        protected Table outgoingHistory;
        protected Dictionary<string, JET_COLUMNID> txsColumns;
        protected Dictionary<string, JET_COLUMNID> queuesColumns;
        protected readonly Dictionary<string, QueueActions> queuesByName = new Dictionary<string, QueueActions>();

        protected AbstractActions(JET_INSTANCE instance, string database)
        {
            session = new Session(instance);

            transaction = new Transaction(session);
            Api.JetOpenDatabase(session, database, null, out dbid, OpenDatabaseGrbit.None);

            queues = new Table(session, dbid, "queues", OpenTableGrbit.None);
            txs = new Table(session, dbid, "transactions", OpenTableGrbit.None);
            recovery = new Table(session, dbid, "recovery", OpenTableGrbit.None);
            outgoing = new Table(session, dbid, "outgoing", OpenTableGrbit.None);
            outgoingHistory = new Table(session, dbid, "outgoing_history", OpenTableGrbit.None);

            queuesColumns = Api.GetColumnDictionary(session, queues);
            txsColumns = Api.GetColumnDictionary(session, txs);
            recoveryColumns = Api.GetColumnDictionary(session, recovery);
            outgoingColumns = Api.GetColumnDictionary(session, outgoing);
            outgoingHistoryColumns = Api.GetColumnDictionary(session, outgoingHistory);
        }

        public QueueActions GetQueue(string queueName)
        {
            QueueActions actions;
            if (queuesByName.TryGetValue(queueName, out actions))
                return actions;

            Api.JetSetCurrentIndex(session, queues, "pk");
            Api.MakeKey(session, queues, queueName, Encoding.Unicode, MakeKeyGrbit.NewKey);

            if (Api.TrySeek(session, queues, SeekGrbit.SeekEQ) == false)
                throw new QueueDoesNotExistsException(queueName);

            queuesByName[queueName] = actions =
                new QueueActions(session, dbid, queueName, i => AddToNumberOfMessagesIn(queueName, i));
            return actions;
        }

        private void AddToNumberOfMessagesIn(string queueName, int count)
        {
            Api.JetSetCurrentIndex(session, queues, "pk");
            Api.MakeKey(session, queues, queueName, Encoding.Unicode, MakeKeyGrbit.NewKey);

            if (Api.TrySeek(session, queues, SeekGrbit.SeekEQ) == false)
                return;

            var bytes = BitConverter.GetBytes(count);
            int actual;
            Api.JetEscrowUpdate(session, queues, queuesColumns["number_of_messages"], bytes, bytes.Length,
                                null, 0, out actual, EscrowUpdateGrbit.None);
        }

        public void Dispose()
        {
            foreach (var action in queuesByName.Values)
            {
                action.Dispose();
            }

            if (queues != null)
                queues.Dispose();
            if (txs != null)
                txs.Dispose();
            if (recovery != null)
                recovery.Dispose();
            if (outgoing != null)
                outgoing.Dispose();
            if (outgoingHistory != null)
                outgoingHistory.Dispose();

            if (Equals(dbid, JET_DBID.Nil) == false)
                Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);

            if (transaction != null)
                transaction.Dispose();

            if (session != null)
                session.Dispose();
        }

        public void Commit()
        {
            transaction.Commit(CommitTransactionGrbit.None);
        }
    }
}