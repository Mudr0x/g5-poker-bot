using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using G5.Logic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace G5.PlayVsBots
{
    public partial class PlayVsBotsForm : Form
    {
        private static readonly int NUM_PLAYERS = 6;

        private OpponentModeling.Options _options;
        private OpponentModeling _opponentModeling;
        private BotGameState[] _botGameStates = new BotGameState[NUM_PLAYERS];
        private Deck _deck = new Deck();

        private TableType _tableType;
        private int _heroInd;
        private int _bigBlindSize;
        private int _startStackSize;
        private int[] _totalInvestedMoney = new int[6];
        private int[] _rebuysCount = new int[6];
        private int _handsPlayed;

        //private Thread t = new Thread(displayState).Start();
        static readonly object locker = new object();

        public PlayVsBotsForm()
        {
            InitializeComponent();

            for (int i = 0; i < _rebuysCount.Length; i++)
                _rebuysCount[i] = 0;
            _handsPlayed = 0;
            _bigBlindSize = 4;
            _options = new OpponentModeling.Options();
            _options.recentHandsCount = 30;

            _tableType = (NUM_PLAYERS == 2) ? TableType.HeadsUp : TableType.SixMax;
            var statListFile = (NUM_PLAYERS == 2) ? "full_stats_list_hu.bin" : "full_stats_list_6max.bin";

            _opponentModeling = new OpponentModeling(statListFile, /*_bigBlindSize,*/ _tableType, _options);

            _gameTableControl.NextButtonPressed += buttonNext_Click;
            _gameTableControl.FoldButtonPressed += buttonFold_Click;
            _gameTableControl.CallButtonPressed += buttonCheckCall_Click;
            _gameTableControl.RaiseButtonPressed += buttonBetRaise_Click;
            _gameTableControl.AllinButtonPressed += buttonAllin_Click;
            _gameTableControl.EditBetTextBoxKeyDown += textBoxEditBet_KeyDown;

            _heroInd = 0;
            _startStackSize = _bigBlindSize * 100;

            String[] playerNames = new string[NUM_PLAYERS];
            int[] stackSizes = new int[NUM_PLAYERS];
            playerNames[0] = "Hero";

            for (int i = 1; i < NUM_PLAYERS; i++)
            {
                playerNames[i] = "Bot" + i.ToString();
                stackSizes[i] = _startStackSize;
            }

            for (int i = 0; i < NUM_PLAYERS; i++)
            {
                _botGameStates[i] = new BotGameState(playerNames, stackSizes, i, 0, _bigBlindSize, PokerClient.G5, _tableType,
                    new Logic.Estimators.ModelingEstimator(_opponentModeling, PokerClient.G5));
            }

            for (int i = 0; i < _totalInvestedMoney.Length; i++)
                _totalInvestedMoney[i] = _startStackSize;
            startNewHand();
            lock (locker)
            {
                displayState();
            }
        }

        private string moneyToString(int money)
        {
            return "$" + (money / 100.0f).ToString("f2");
        }

        private List<Player> getPlayers()
        {
            return _botGameStates[_heroInd].getPlayers();
        }

        private void displayState()
        {
            int heroStack = getPlayers()[_heroInd].Stack;
            //this.Text = "Play vs Bots (Stack: " + moneyToString(heroStack - _totalInvestedMoney[_heroInd]) + ", Blinds: " + moneyToString(_bigBlindSize/2) + "/" + moneyToString(_bigBlindSize) + ")";
            double bb100 = (((heroStack - _totalInvestedMoney[_heroInd]) / _bigBlindSize) * 100) / _handsPlayed;
            this.Text = "Play vs Bots ( " + bb100.ToString() +  "bb/100, Blinds: " + moneyToString(_bigBlindSize / 2) + "/" + moneyToString(_bigBlindSize) + " )";

            int playerToActInd = _botGameStates[_heroInd].getPlayerToActInd();
            int buttonInd = _botGameStates[_heroInd].getButtonInd();

            for (int i = 0; i < NUM_PLAYERS; i++)
                _gameTableControl.hidePlayerInfo(i);

            for (int i = 0; i < NUM_PLAYERS; i++)
            {
                HoleCards hh = null;
                var street = _botGameStates[_heroInd].getStreet();

                if (i == _heroInd ||
                    playerToActInd < 0 && street == Street.River ||
                    _botGameStates[_heroInd].numActiveNonAllInPlayers() == 0 /*||
                    _botGameStates[_heroInd].getPlayers()[_heroInd].StatusInHand == Status.Folded*/)
                {
                    hh = _botGameStates[i].getHeroHoleCards();
                }

                Player player = getPlayers()[i];
                int wherePlayerSits = (NUM_PLAYERS == 2) ? (i * 3) : i;

                _gameTableControl.updatePlayerInfo(wherePlayerSits, player.Name, player.Stack, player.BetAmount, player.StatusInHand, player.LastAction, hh,
                    player.PreFlopPosition, (playerToActInd == i));

                if (i == buttonInd)
                    _gameTableControl.setButtonPosition(wherePlayerSits);
            }
            
            _gameTableControl.disablePlayerControls();
            _gameTableControl.setbuttonNextEnabled(true);

            if (playerToActInd == _heroInd && (_botGameStates[_heroInd].numActivePlayers() >= 2))
            {
                _gameTableControl.enablePlayerControls();
                _gameTableControl.setbuttonNextEnabled(false);

                _gameTableControl.setupPlayerControls(_botGameStates[_heroInd].getNumBets(), _botGameStates[_heroInd].getAmountToCall(),
                    _botGameStates[_heroInd].getRaiseAmount(), getPlayers()[_heroInd].Stack);
            }

            _gameTableControl.setPotSize(_botGameStates[_heroInd].potSize());
            _gameTableControl.displayBoard(_botGameStates[_heroInd].getBoard().Cards);
            if (_gameTableControl.buttonNext.Enabled)
            {
                System.Threading.Thread.Sleep(900);
                buttonNext_Click(this, null);
            }
        }

        private void finishHand()
        {
            var handStrengths = new List<int>();

            foreach (var botGameState in _botGameStates)
            {
                if (_botGameStates[0].getBoard().Count == 5) // TODO CHeeeeeeeeeeck
                {
                    var handStrength = botGameState.calculateHeroHandStrength();
                    handStrengths.Add(handStrength.Value());
                }
                else
                {
                    handStrengths.Add(0);
                }
            }

            var winnings = Pot.calculateWinnings(getPlayers(), handStrengths);

            _gameTableControl.log("** Summary **");
            for (int i = 0; i < _botGameStates.Length; i++)
            {
                var player = getPlayers()[i];
                _gameTableControl.log(player.Name + " shows [ " +_botGameStates[i].getHeroHoleCards().Card0 + ", " + _botGameStates[i].getHeroHoleCards().Card1 + " ]");
            }

            for (int i = 0; i < winnings.Count; i++)
            {
                if (winnings[i] > 0)
                {
                    int winning;
                    var player = getPlayers()[i];
                    if (player.PreFlopPosition == Position.SB)
                        winning = winnings[i] - 2;
                    else if (player.PreFlopPosition == Position.BB)
                        winning = winnings[i] - 4;
                    else
                        winning = winnings[i];
                    _gameTableControl.log(player.Name + " collected [ " + moneyToString(winning) + " ]");
                }
            }
            // End of hand line break
            _gameTableControl.log("");

            foreach (var botGameState in _botGameStates)
                botGameState.finishHand(winnings);

            _opponentModeling.addHand(_botGameStates[0].getCurrentHand());
        }

        public int getHandsPlayed()
        {
            return _handsPlayed;
        }

        private void startNewHand()
        {
            _handsPlayed++;
            _gameTableControl.updateHandsPlayed(_handsPlayed);
            List<int> sittingOutPlayers = new List<int>();

            DateTime currentDateTime = DateTime.Now;
            string rawDateTime = currentDateTime.ToString("FFFFFFF");
            string formattedDateTime = currentDateTime.ToString("dd MM yyyy HH:mm:ss");
            _gameTableControl.log("#Game No : " + rawDateTime);
            _gameTableControl.log("***** 888poker Hand History for Game " + rawDateTime + " *****");
            _gameTableControl.log("$0.02/$0.04 Blinds No Limit Holdem - *** " + formattedDateTime);
            _gameTableControl.log("Table Athens 6 Max (Real Money)");

            for (int i = 0; i < NUM_PLAYERS; i++)
            {
                var player = getPlayers()[i];

                if (player.Stack < _bigBlindSize * 2)
                {
                    if (_gameTableControl.getNoRebuy() && i != _heroInd && _handsPlayed > 1)
                    {
                        sittingOutPlayers.Add(i);
                    }
                    else
                    {
                        foreach (var botGameState in _botGameStates)
                            botGameState.playerBringsIn(i, _startStackSize);

                        //string pos = " (" + getPlayers()[_heroInd].PreFlopPosition.ToString() + ")";
                        //_gameTableControl.log(player.Name + pos + " Brings in " + moneyToString(_startStackSize));

                        if (_handsPlayed > 1)
                        {
                            _totalInvestedMoney[i] += _startStackSize;
                            _rebuysCount[i]++;
                        }
                    }
                }
                int playerStack = player.Stack;
                double bb100 = (((playerStack - _totalInvestedMoney[i]) / _bigBlindSize) * 100) / _handsPlayed;
                _gameTableControl.updatebbWon(i, _rebuysCount[i], bb100);
            }

            _deck.reset();

            for (int i = 0; i < _botGameStates.Length; i++)
            {
                var player = getPlayers()[i];
                if (player.PreFlopPosition == Position.BU)
                    _gameTableControl.log("Seat " + (i+1).ToString() + " is the button");
            }
            _gameTableControl.log("Total number of players : " + _botGameStates.Length);
            for (int i = 0; i < _botGameStates.Length; i++)
            {
                _botGameStates[i].startNewHand(sittingOutPlayers);
                _botGameStates[i].dealHoleCards(_deck.dealCard(), _deck.dealCard());
                var player = getPlayers()[i];
                int playerStack = player.Stack;
                string playerName = player.Name;
                //string position = " (" + player.PreFlopPosition.ToString() + ")";
                _gameTableControl.log("Seat "+ (i+1).ToString() + ": " + playerName + " ( " + moneyToString(playerStack) + " )");
            }
            for (int i = 0; i < _botGameStates.Length; i++)
            {
                var player = getPlayers()[i];
                if (player.PreFlopPosition == Position.SB)
                    _gameTableControl.log(player.Name + " posts small blind [" + moneyToString(player.BetAmount) + "]");
                if (player.PreFlopPosition == Position.BB)
                    _gameTableControl.log(player.Name + " posts big blind [" + moneyToString(player.BetAmount) + "]");
            }

            _gameTableControl.log("** Dealing down cards **");
            string holeCards = _botGameStates[_heroInd].getHeroHoleCards().Card0.ToString() + ", " + _botGameStates[_heroInd].getHeroHoleCards().Card1.ToString();
            _gameTableControl.log("Dealt to " + getPlayers()[_heroInd].Name + " [ " + holeCards + " ]");
        }

        private void nextStreet(Street street)
        {
            int numCardsToDraw = (street == Street.PreFlop) ? 3 : 1;
            List<Card> cards = new List<Card>();
            var cardStr = "";

            for (int i = 0; i < numCardsToDraw; i++)
            {
                var card = _deck.dealCard();
                cards.Add(card);
                if (i == numCardsToDraw - 1)
                    cardStr += card.ToString();
                else
                    cardStr += card.ToString() + ", ";
            }

            _gameTableControl.log("** Dealing " + (street + 1).ToString().ToLower() + " ** [ " + cardStr + " ]");

            foreach (var botGameState in _botGameStates)
                botGameState.goToNextStreet(cards);
        }

        private void playerCheckCalls()
        {
            foreach (var botGameState in _botGameStates)
                botGameState.playerCheckCalls();
        }

        private void playerBetRaisesBy(int amount)
        {
            foreach (var botGameState in _botGameStates)
                botGameState.playerBetRaisesBy(amount);
        }

        private void playerFolds()
        {
            foreach (var botGameState in _botGameStates)
                botGameState.playerFolds();
        }

        private void playerGoesAllIn()
        {
            foreach (var botGameState in _botGameStates)
                botGameState.playerGoesAllIn();
        }

        private void buttonNext_Click(object sender, EventArgs e)
        {
            int playerToActInd = _botGameStates[_heroInd].getPlayerToActInd();

            if (playerToActInd < 0)
            {
                Street street = _botGameStates[_heroInd].getStreet();

                if (street == Street.River)
                {
                    finishHand();
                    startNewHand();
                }
                else if (_botGameStates[_heroInd].numActivePlayers() >= 2)
                {
                    nextStreet(street);
                }
                else
                {
                    finishHand();
                    startNewHand();
                }
            }
            else
            {
                Debug.Assert(playerToActInd != _heroInd);

                var bd = _botGameStates[playerToActInd].calculateHeroAction();

                //double amount = Convert.ToDouble(bd.byAmount) / 100;
                //var workTimeStr = "[" + bd.timeSpentSeconds.ToString("f2") + "s]";
                var actionStr = "";

                if (bd.actionType == ActionType.Fold)
                {
                    actionStr = " folds";
                    playerFolds();
                }
                else if (bd.actionType == ActionType.Check || bd.actionType == ActionType.Call)
                {
                    if (bd.actionType == ActionType.Check)
                        actionStr = " checks";

                    if (bd.actionType == ActionType.Call)
                        actionStr = " calls [" + moneyToString(bd.byAmount) + "]";

                    playerCheckCalls();
                }
                else if (bd.actionType == ActionType.Bet || bd.actionType == ActionType.Raise || bd.actionType == ActionType.AllIn)
                {
                    if (bd.actionType == ActionType.Bet)
                        actionStr = " bets [" + moneyToString(bd.byAmount) + "]";

                    if (bd.actionType == ActionType.Raise)
                        actionStr = " raises [" + moneyToString(bd.byAmount) + "]";

                    if (bd.actionType == ActionType.AllIn)
                        actionStr = ((_botGameStates[_heroInd].getNumBets() > 0) ? " raises [" : " bets [") + moneyToString(bd.byAmount) + "]";

                    playerBetRaisesBy(bd.byAmount);
                }

                var botName = getPlayers()[playerToActInd].Name;
                _gameTableControl.log(botName + actionStr);
            }
            lock (locker)
            {
                displayState();
            }
        }

        private void buttonFold_Click(object sender, EventArgs e)
        {
            _gameTableControl.log(_botGameStates[_heroInd].getPlayerToAct().Name + " folds");
            playerFolds();
            lock (locker)
            {
                displayState();
            }
        }

        private void buttonCheckCall_Click(object sender, EventArgs e)
        {
            int callAmount = _botGameStates[_heroInd].getAmountToCall();
            var actionStr = (callAmount > 0) ? " calls [" + moneyToString(callAmount) + "]" : " checks";
            _gameTableControl.log(_botGameStates[_heroInd].getPlayerToAct().Name + actionStr);
            playerCheckCalls();
            lock (locker)
            {
                displayState();
            }
        }

        private void buttonBetRaise_Click(object sender, int raiseAmount)
        {
            var ra = (raiseAmount == 0) ? _botGameStates[_heroInd].getRaiseAmount() : raiseAmount;
            var actionStr = (_botGameStates[_heroInd].getNumBets() > 0) ? " raises [" : " bets [" + moneyToString(raiseAmount) + "]";
            _gameTableControl.log(_botGameStates[_heroInd].getPlayerToAct().Name + actionStr);

            playerBetRaisesBy(ra);
            lock (locker)
            {
                displayState();
            }
        }

        private void buttonAllin_Click(object sender, int raiseAmount)
        {
            _gameTableControl.log(_botGameStates[_heroInd].getPlayerToAct().Name + ((_botGameStates[_heroInd].getNumBets() > 0) ? " raises [" : " bets [") + moneyToString(raiseAmount) + "]");
            playerGoesAllIn();
            lock (locker)
            {
                displayState();
            }
        }

        private void textBoxEditBet_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Return)
            {
                string str_betsize = _gameTableControl.textBoxEditBet.Text;
                int raiseAmount = Convert.ToInt32(Convert.ToDouble(str_betsize) * 100);
                var actionStr = ((_botGameStates[_heroInd].getNumBets() > 0) ? " raises [" : " bets [") + moneyToString(raiseAmount) + "]";
                _gameTableControl.log(_botGameStates[_heroInd].getPlayerToAct().Name + actionStr);

                playerBetRaisesBy(raiseAmount);
                lock (locker)
                {
                    displayState();
                }
            }
        }
    }
}
