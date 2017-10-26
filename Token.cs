using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NERA1
{
    class Token
    {
        public string occurence;
        public string lemma;
        public string pos;

        public Token(string occurence, string lemma, string pos)
        {
            this.occurence = occurence;
            this.lemma = lemma;
            this.pos = pos;
        }
    }
}
