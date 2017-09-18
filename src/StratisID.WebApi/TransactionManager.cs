using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StratisID.WebApi
{
    public class TransactionManager
    {
        public void SendIdentiyProviderTransaction()
        {
            string userInfo = "Sato Naka|nakamoto.sata@outlook.com|1760eea9-2a86-43d1-9ec2-80149f315776";
            byte[] userInfoHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(userInfo));
            byte[] msgBlockchain = Encoding.UTF8.GetBytes("MSFT").Concat(userInfoHash).ToArray();

            string LastTxId = "7d798bd2a29d2ba759883c27c4d01556e2d013380edfa79c80464e62a8f90b70";
            string SourcePublicAddress = "STrATiSwHPf36VbqWMUaduaN57A791YP9c";
            string SourcePrivateKey = "EnterYourPrivateKey"; 
            string DestinationAddress = "SdMCMmLjD6NK8ssWt5nH2gtv6XkQXErBRs";
            double amountTx = 0.0001;
            double feeTx = 0.0001;

            var transaction = SendWallMessage(
                LastTxId,
                SourcePublicAddress,
                SourcePrivateKey,
                DestinationAddress,
                amountTx,
                feeTx,
                msgBlockchain);

            Console.WriteLine(transaction);
            Console.ReadKey(true);
        }

        public RPCClient GetRPC()
        {
            // Be sure you have the following parameters enabled on the Stratis node (default port: 16174)
            // server=1
            // rpcbind=127.0.0.1
            // rpcallowip=ip.address.doing.request
            // rpcuser=user
            // rpcpassword=nodePassword

            // The information (ip address) and Credential to connect to the node
            var credentials = new NetworkCredential("user", "nodePassword");
            return new RPCClient(credentials, new Uri("http://127.0.0.1:16174/"), Network.StratisMain);
        }

        public Transaction SendWallMessage(string sourceTxId, string sourcePublicAddress,
            string stratPrivateKey, string destinationAddress, double amountTx, double feeTx, byte[] bytesMsg)
        {
            // RPC Connection to Stratis Blockchain
            var rpc = GetRPC();

            // Can either use the raw transaction hex from a wallet's getrawtransaction CLI command, or look up the equivalent information via RPC
            Transaction tx = rpc.GetRawTransaction(uint256.Parse(sourceTxId));

            // The destination address will receive 0.0001 STRAT
            BitcoinAddress destAddress = BitcoinAddress.Create(destinationAddress, Network.StratisMain);

            // Stratis Source Address - The source address is used to store the 'change' from the transaction - THIS IS THE SIGNATURE for the attestation
            BitcoinAddress sourceAddress = BitcoinAddress.Create(sourcePublicAddress, Network.StratisMain);

            // The private key must be the key for the source address to be able to send funds from the source address (String of ~52 ASCII) 
            BitcoinSecret sourcePrivateKey = new BitcoinSecret(stratPrivateKey, Network.StratisMain);

            int outputIndex = 0;
            int indexTx = 0;
            TxOutList listOutTx = tx.Outputs;

            foreach (var item in listOutTx)
            {
                string opCode = item.ScriptPubKey.ToString();
                if (opCode.StartsWith("OP_DUP OP_HASH160"))
                {
                    string sAddress = new Script(opCode).GetDestinationAddress(Network.StratisMain).ToString();
                    if (sAddress.Equals(sourcePublicAddress))
                    {
                        outputIndex = indexTx;
                    }
                }
                ++indexTx;
            }

            // For the fee to be correctly calculated, the quantity of funds in the source transaction needs to be known
            Money remainingBalance = tx.Outputs[outputIndex].Value;

            // Now that the source Transaction is obtained, the right output needs to be selected as an input for the new transaction. 
            OutPoint outPoint = new OutPoint(tx, outputIndex);

            // The source transaction's output (must be unspent) is the input for the new transaction
            Transaction sendTx = new Transaction();
            sendTx.Inputs.Add(new TxIn()
            {
                PrevOut = outPoint
            });

            // Can currently only send a maximum of 40 bytes in the null data transaction (bytesMsg)
            // Also note that a nulldata transaction currently has to have a nonzero value assigned to it.
            TxOut messageTxOut = new TxOut()
            {
                Value = new Money((decimal) 0.0001, MoneyUnit.BTC),
                ScriptPubKey = TxNullDataTemplate.Instance.GenerateScriptPubKey(bytesMsg)
            };

            // For Attestation amountTx = 0.0001 STRAT is being sent to destAddress
            TxOut destTxOut = new TxOut()
            {
                Value = new Money((decimal) amountTx, MoneyUnit.BTC),
                ScriptPubKey = destAddress.ScriptPubKey
            };
            double discBalance = feeTx + amountTx + 0.0001; // 0.0001 : nulldata transaction amount

            // This is what subsequent transactions use to prove ownership of the funds (more specifically, the private key used to create the ScriptPubKey is known)
            // Send the change back to the originating address. 
            TxOut changeBackTxOut = new TxOut()
            {
                Value = new Money(((remainingBalance.ToDecimal(MoneyUnit.BTC) - (decimal) discBalance)), MoneyUnit.BTC),
                ScriptPubKey = sourceAddress.ScriptPubKey
            };
            // changeBackTxOut = remainingBalance - 0.0001 (sent) - 0.0001 (network fee) - 0.0001 (nulldata)
            // Transactions without fees may violate consensus rules, or may not be relayed by other nodes on the network.

            // Add the outputs to the transaction being built
            sendTx.Outputs.Add(destTxOut);
            sendTx.Outputs.Add(messageTxOut);
            sendTx.Outputs.Add(changeBackTxOut);

            // Signing the transaction
            sendTx.Inputs[0].ScriptSig = sourceAddress.ScriptPubKey;

            // Sign the transaction using the specified private key
            sendTx.Sign(sourcePrivateKey, false);

            // Broadcast Transaction 
            rpc.SendRawTransactionAsync(sendTx);

            return sendTx;
        }
    }

    class Program
    {
        private static void Main(string[] args)
        {
            TransactionManager newTransaction = new TransactionManager();
            newTransaction.SendIdentiyProviderTransaction();
        }
    }
}
